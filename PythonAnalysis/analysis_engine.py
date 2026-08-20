# -*- coding: utf-8 -*-
"""UpperMachine 数据分析引擎（供 C# 上位机通过子进程调用）。

忠实复刻 core_pipeline.run_analysis 的【计算部分】，但不做 matplotlib 绘图、
不弹窗、不 plt.show()。结果以 JSON 输出，交给 C# 端用 ScottPlot / HelixToolkit
做交互式渲染。

用法：
    python analysis_engine.py --csv <输入CSV> --json <输出JSON> --algo <iforest|lof|dbscan|baseline>
"""
import argparse
import json
import math
import os
import sys

import matplotlib
matplotlib.use("Agg")  # 必须在导入 core_pipeline(会 import matplotlib.pyplot) 之前

import numpy as np
import pandas as pd
from scipy.ndimage import median_filter, laplace, binary_dilation
from scipy.interpolate import griddata

from core_pipeline import identify_electrode_region, detect_outliers_iqr
from anomaly_detectors import get_detector


# --------------------------------------------------------------------------- #
# JSON 序列化辅助：numpy 标量 → Python 原生；NaN/Inf → null（None）
# --------------------------------------------------------------------------- #
def _num(x):
    if x is None:
        return None
    try:
        f = float(x)
    except (TypeError, ValueError):
        return None
    if math.isnan(f) or math.isinf(f):
        return None
    return f


def _grid(a):
    """二维 numpy 数组 → list[list[float|None]]，NaN 转 null。"""
    return [[_num(v) for v in row] for row in a]


def _flat(a):
    """一维 numpy 数组（无 NaN）→ list[float]。"""
    return [float(v) for v in a]


# --------------------------------------------------------------------------- #
# 列名规范化：兼容 X坐标/Y坐标/电压值、X/Y/Voltage、小写 x/y/v、前三列兜底
# --------------------------------------------------------------------------- #
def normalize_columns(df):
    if {"X坐标", "Y坐标", "电压值"}.issubset(df.columns):
        return df.rename(columns={"X坐标": "X", "Y坐标": "Y", "电压值": "Voltage"})[["X", "Y", "Voltage"]]
    if {"X", "Y", "Voltage"}.issubset(df.columns):
        return df[["X", "Y", "Voltage"]]
    lower_map = {str(c).strip().lower(): c for c in df.columns}
    if {"x", "y", "v"}.issubset(lower_map.keys()):
        return df.rename(columns={lower_map["x"]: "X", lower_map["y"]: "Y", lower_map["v"]: "Voltage"})[["X", "Y", "Voltage"]]
    if {"x", "y", "voltage"}.issubset(lower_map.keys()):
        return df.rename(columns={lower_map["x"]: "X", lower_map["y"]: "Y", lower_map["voltage"]: "Voltage"})[["X", "Y", "Voltage"]]
    if df.shape[1] >= 3:
        d = df.iloc[:, :3].copy()
        d.columns = ["X", "Y", "Voltage"]
        return d
    raise ValueError(f"无法识别 CSV 列名: {list(df.columns)}")


# --------------------------------------------------------------------------- #
# PINN（可选）：加载已有模型并预测。任何失败都返回 None，不中断主流程。
# --------------------------------------------------------------------------- #
def _find_pretrained_models():
    """在脚本目录及其上级“输出”目录里查找 .pth 模型文件。"""
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = []
    for base in (here, os.path.normpath(os.path.join(here, "..", "输出"))):
        if os.path.isdir(base):
            for name in os.listdir(base):
                if name.lower().endswith(".pth"):
                    candidates.append(os.path.join(base, name))
    return candidates


def _try_load_pretrained(X_unique, Y_unique, Z_cleaned):
    """加载已有 .pth 模型并预测网格；找不到模型或加载失败返回 None。"""
    try:
        import torch
        import pinn_predict
    except Exception:
        return None

    try:
        candidates = _find_pretrained_models()
        if not candidates:
            return None

        device = "cuda" if torch.cuda.is_available() else "cpu"
        model, scaler_x, scaler_y = pinn_predict.load_model(candidates[0], device)
        if scaler_x is None or scaler_y is None:
            return None  # 模型里没有 scaler，无法反归一化，跳过

        # 在网格点上预测，便于 C# 端画热力图/3D 对比
        xg, yg = np.meshgrid(X_unique, Y_unique)
        X_flat = np.column_stack([xg.ravel(), yg.ravel()])
        y_pred = pinn_predict.predict_on_plane(model, scaler_x, scaler_y, X_flat, device)
        pred_grid = y_pred.reshape(len(Y_unique), len(X_unique))
        true_flat = Z_cleaned.ravel()
        resid = y_pred - true_flat
        mse = float(np.mean(resid ** 2))
        mae = float(np.mean(np.abs(resid)))

        return {
            "x": _flat(X_unique),
            "y": _flat(Y_unique),
            "predicted": _grid(pred_grid),
            "true": _grid(Z_cleaned),
            "rmse": math.sqrt(mse),
            "mae": mae,
        }
    except Exception:
        return None


def train_fast_pinn(X_unique, Y_unique, Z_cleaned):
    """没有预训练模型时，在线训练一个小型 PINN 逼近清洗后的电压场（约 1~2 秒）。

    与 pinn.py 同网络结构（AdaptivePINN），只是缩小规模、减少轮次以适配上位机
    的 CPU 实时使用；物理约束项（PDE loss）二阶自动微分太慢，这里只保留数据损失，
    小网络 + tanh 本身就能得到足够光滑的拟合曲面。
    """
    import torch
    from sklearn.preprocessing import MinMaxScaler
    from pinn_predict import AdaptivePINN

    xg, yg = np.meshgrid(X_unique, Y_unique)
    X_all = np.column_stack([xg.ravel(), yg.ravel()]).astype(np.float32)
    V_all = Z_cleaned.ravel().astype(np.float32)
    valid = ~np.isnan(V_all)
    if np.sum(valid) < 4:
        return None

    X = X_all[valid]
    V = V_all[valid].reshape(-1, 1)

    scaler_x = MinMaxScaler(feature_range=(-1, 1))
    scaler_y = MinMaxScaler(feature_range=(0, 1))
    X_norm = scaler_x.fit_transform(X).astype(np.float32)
    V_norm = scaler_y.fit_transform(V).astype(np.float32)

    torch.manual_seed(0)
    model = AdaptivePINN(input_dim=2, hidden_dim=64, out_dim=1, num_res_blocks=3)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-3)
    X_t = torch.tensor(X_norm)
    V_t = torch.tensor(V_norm)
    for _ in range(400):
        pred = model(X_t)
        loss = torch.mean((pred - V_t) ** 2)
        optimizer.zero_grad()
        loss.backward()
        optimizer.step()

    model.eval()
    with torch.no_grad():
        X_grid_norm = torch.tensor(scaler_x.transform(X_all).astype(np.float32))
        pred_norm = model(X_grid_norm).cpu().numpy()
    pred_grid = scaler_y.inverse_transform(pred_norm).reshape(len(Y_unique), len(X_unique))

    true_flat = Z_cleaned.ravel()
    mask = ~np.isnan(true_flat)
    resid = pred_grid.ravel()[mask] - true_flat[mask]
    mse = float(np.mean(resid ** 2))
    mae = float(np.mean(np.abs(resid)))

    return {
        "x": _flat(X_unique),
        "y": _flat(Y_unique),
        "predicted": _grid(pred_grid),
        "true": _grid(Z_cleaned),
        "rmse": math.sqrt(mse),
        "mae": mae,
    }


def try_pinn(X_unique, Y_unique, Z_cleaned):
    """优先加载已有模型；没有模型则在线训练一个小型 PINN。任何失败返回 None。"""
    result = _try_load_pretrained(X_unique, Y_unique, Z_cleaned)
    if result is not None:
        return result
    try:
        return train_fast_pinn(X_unique, Y_unique, Z_cleaned)
    except Exception:
        return None


# --------------------------------------------------------------------------- #
# 主分析流程（复刻 core_pipeline.run_analysis 计算部分）
# --------------------------------------------------------------------------- #
def run_analysis(csv_path, algo):
    df = pd.read_csv(csv_path)
    df = normalize_columns(df)
    if df.duplicated(subset=["X", "Y"]).sum() > 0:
        df = df.drop_duplicates(subset=["X", "Y"], keep="last")
    df = df.dropna()
    if len(df) == 0:
        raise ValueError("CSV 中没有有效测量点。")

    # 1. 全局电压异常移除（IQR）
    v_outliers = detect_outliers_iqr(df["Voltage"].values)
    df = df[~v_outliers]
    if len(df) == 0:
        raise ValueError("IQR 离群剔除后没有剩余数据点。")

    X_original = df["X"].values.astype(float)
    Y_original = df["Y"].values.astype(float)
    Z_voltage = df["Voltage"].values.astype(float)
    n_points = len(df)

    # 2. 电极区域识别
    electrode_points, v_max, v_min = identify_electrode_region(X_original, Y_original, Z_voltage)
    non_electrode_mask = ~electrode_points

    # 3. 孤立异常识别（低电压 + 空间异常检测算法）
    isolated_outliers = np.zeros(n_points, dtype=bool)
    if np.sum(non_electrode_mask) > 20:
        v_anomaly_mask = Z_voltage <= 0.01
        isolated_outliers[non_electrode_mask] |= v_anomaly_mask[non_electrode_mask]

        non_v_mask = ~v_anomaly_mask
        valid_spatial_mask = non_electrode_mask & non_v_mask

        if np.sum(valid_spatial_mask) > 20:
            features = np.column_stack([
                X_original[valid_spatial_mask],
                Y_original[valid_spatial_mask],
                Z_voltage[valid_spatial_mask],
            ])
            detector = get_detector(algo, {})
            spatial_anomaly_mask = detector(features)
            isolated_outliers[valid_spatial_mask] |= spatial_anomaly_mask

    n_normal = int(np.sum(~(electrode_points | isolated_outliers)))
    n_electrode = int(np.sum(electrode_points))
    n_isolated = int(np.sum(isolated_outliers))

    # 4. 网格化与去噪
    df_pivot = df.pivot(index="Y", columns="X", values="Voltage")
    X_unique = df_pivot.columns.values.astype(float)
    Y_unique = df_pivot.index.values.astype(float)
    Z_raw = df_pivot.values.astype(float)

    if np.isnan(Z_raw).any():
        y_coords, x_coords = np.meshgrid(Y_unique, X_unique, indexing="ij")
        valid_mask = ~np.isnan(Z_raw)
        pts = np.column_stack((x_coords[valid_mask], y_coords[valid_mask]))
        vals = Z_raw[valid_mask]
        grid_pts = np.column_stack((x_coords.ravel(), y_coords.ravel()))
        Z_raw = griddata(pts, vals, grid_pts, method="linear").reshape(Z_raw.shape)
        if np.isnan(Z_raw).any():
            Z_raw = griddata(pts, vals, grid_pts, method="nearest").reshape(Z_raw.shape)

    Z_cleaned = median_filter(Z_raw, size=5)

    # 5. 剔除区域掩码（映射回网格）
    df_copy = df.copy()
    df_copy["Exclude"] = electrode_points | isolated_outliers
    exclude_pivot = df_copy.pivot(index="Y", columns="X", values="Exclude").values
    remove_mask = np.nan_to_num(exclude_pivot, nan=False).astype(bool)
    keep_mask = ~remove_mask

    # 6. 电场矢量
    dy = np.mean(np.diff(Y_unique)) if len(Y_unique) > 1 else 1.0
    dx = np.mean(np.diff(X_unique)) if len(X_unique) > 1 else 1.0
    Ey, Ex = np.gradient(-Z_cleaned, dy, dx)
    E_mag = np.sqrt(Ex ** 2 + Ey ** 2) + 1e-10
    Ex_norm, Ey_norm = Ex / E_mag, Ey / E_mag

    x_mesh, y_mesh = np.meshgrid(X_unique, Y_unique)
    skip = 3
    x_arrow = x_mesh[::skip, ::skip]
    y_arrow = y_mesh[::skip, ::skip]
    Ex_arrow = Ex_norm[::skip, ::skip]
    Ey_arrow = Ey_norm[::skip, ::skip]
    mask_arrow = keep_mask[::skip, ::skip]
    x_arrow = x_arrow[mask_arrow]
    y_arrow = y_arrow[mask_arrow]
    Ex_arrow = Ex_arrow[mask_arrow]
    Ey_arrow = Ey_arrow[mask_arrow]

    # 7. 拉普拉斯与统计
    Z_laplace_full = laplace(Z_cleaned)
    Z_laplace = Z_laplace_full.copy()
    expanded_remove_mask = binary_dilation(remove_mask, iterations=1)
    Z_laplace[expanded_remove_mask] = np.nan

    volt_mean = _num(np.round(np.mean(Z_cleaned[keep_mask]), 8)) if np.any(keep_mask) else 0.0
    lap_mean = _num(np.round(np.nanmean(Z_laplace), 8))
    lap_std = _num(np.round(np.nanstd(Z_laplace), 8))
    lap_data = Z_laplace[~np.isnan(Z_laplace)].flatten()

    # 8. 逐点分类结果
    points = []
    for i in range(n_points):
        if electrode_points[i]:
            category = "electrode"
        elif isolated_outliers[i]:
            category = "anomaly"
        else:
            category = "normal"
        points.append({
            "x": _num(X_original[i]),
            "y": _num(Y_original[i]),
            "v": _num(Z_voltage[i]),
            "category": category,
        })

    # 9. 可选 PINN
    pinn = try_pinn(X_unique, Y_unique, Z_cleaned)

    return {
        "algo": algo,
        "counts": {
            "normal": n_normal,
            "electrode": n_electrode,
            "anomaly": n_isolated,
        },
        "points": points,
        "grid": {
            "x": _flat(X_unique),
            "y": _flat(Y_unique),
            "cleaned": _grid(Z_cleaned),
            "laplace": _grid(Z_laplace),
        },
        "electricField": {
            "x": _flat(x_arrow),
            "y": _flat(y_arrow),
            "ex": _flat(Ex_arrow),
            "ey": _flat(Ey_arrow),
        },
        "laplacian": {
            "values": _flat(lap_data),
            "mean": lap_mean,
            "std": lap_std,
            "min": _num(np.min(lap_data)) if len(lap_data) else 0.0,
            "max": _num(np.max(lap_data)) if len(lap_data) else 0.0,
            "samples": int(len(lap_data)),
        },
        "voltageMean": volt_mean,
        "pinn": pinn,
    }


def main():
    parser = argparse.ArgumentParser(description="UpperMachine 数据分析引擎")
    parser.add_argument("--csv", required=True, help="输入 CSV 路径")
    parser.add_argument("--json", required=True, help="输出 JSON 路径")
    parser.add_argument("--algo", default="iforest",
                        choices=["iforest", "lof", "dbscan", "baseline"],
                        help="异常检测算法")
    args = parser.parse_args()

    try:
        result = run_analysis(args.csv, args.algo)
    except Exception as exc:
        print(f"__ERROR__:{exc}", file=sys.stderr)
        sys.exit(1)

    with open(args.json, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, allow_nan=False)

    print(f"__DONE__ counts={result['counts']} samples={result['laplacian']['samples']}")


if __name__ == "__main__":
    main()
