import os
import argparse
import glob
from typing import Optional
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import torch
import torch.nn as nn

plt.rcParams['font.sans-serif'] = ['SimHei', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False

# ===================== 常量配置 =====================
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_NAME = "pinn_voltage_model.pth"
DEFAULT_DATA_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "数据"))
DEFAULT_OUTPUT_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "输出"))
DEFAULT_MODEL_PATH = os.path.normpath(os.path.join(DEFAULT_OUTPUT_DIR, MODEL_NAME))
LEGACY_MODEL_PATH = os.path.join(SCRIPT_DIR, MODEL_NAME)
OUTPUT_IMAGE = "pinn_prediction.png"


def resolve_input_csv(data_dir: str, csv_arg: Optional[str]) -> str:
    if csv_arg:
        path = os.path.abspath(os.path.expanduser(csv_arg.strip()))
        if not os.path.isfile(path):
            raise FileNotFoundError(f"❌ 指定的 CSV 不存在：{path}")
        return path
    data_dir = os.path.abspath(data_dir)
    if not os.path.isdir(data_dir):
        raise FileNotFoundError(f"❌ 数据目录不存在：{data_dir}")
    pattern = os.path.join(data_dir, "*.csv")
    files = sorted(glob.glob(pattern), key=os.path.getmtime, reverse=True)
    if not files:
        raise FileNotFoundError(
            f"❌ 在数据目录中未找到 CSV 文件。\n  目录：{data_dir}\n  匹配：{pattern}"
        )
    return files[0]


def resolve_model_path(explicit: Optional[str]) -> str:
    if explicit:
        p = os.path.abspath(os.path.expanduser(explicit.strip()))
        if not os.path.isfile(p):
            raise FileNotFoundError(f"未找到指定模型：{p}")
        return p
    if os.path.isfile(DEFAULT_MODEL_PATH):
        return DEFAULT_MODEL_PATH
    if os.path.isfile(LEGACY_MODEL_PATH):
        print(f"⚠️ 未在输出目录找到模型，使用脚本目录下的旧版文件：{LEGACY_MODEL_PATH}")
        return LEGACY_MODEL_PATH
    raise FileNotFoundError(
        "未找到模型文件。请先运行 pinn.py 生成模型，或指定 --model。\n"
        f"  已查找：{DEFAULT_MODEL_PATH}\n"
        f"  已查找：{LEGACY_MODEL_PATH}"
    )


def parse_args():
    p = argparse.ArgumentParser(description="自适应 PINN 电压场预测")
    p.add_argument("--data-dir", type=str, default=DEFAULT_DATA_DIR)
    p.add_argument("--csv", type=str, default=None)
    p.add_argument("--output-dir", type=str, default=DEFAULT_OUTPUT_DIR)
    p.add_argument("--model", type=str, default=None)
    p.add_argument("--elev", type=float, default=28.0)
    p.add_argument("--azim", type=float, default=-55.0)
    return p.parse_args()


# ===================== 🔥 完全兼容原模型的自适应残差块 =====================
class ResidualBlock(nn.Module):
    def __init__(self, dim: int):
        super().__init__()
        self.fc1 = nn.Linear(dim, dim)
        self.fc2 = nn.Linear(dim, dim)
        self.act = nn.Tanh()

    def forward(self, x):
        return self.act(self.fc2(self.act(self.fc1(x))) + x)


# ===================== 🔥 自适应 PINN（层命名100%匹配旧模型） =====================
class AdaptivePINN(nn.Module):
    def __init__(self, input_dim=2, hidden_dim=128, out_dim=1, num_res_blocks=6):
        super().__init__()
        # 层命名和你原来的模型完全一样
        self.input_layer = nn.Linear(input_dim, hidden_dim)
        self.res_blocks = nn.ModuleList([ResidualBlock(hidden_dim) for _ in range(num_res_blocks)])
        self.output_layer = nn.Linear(hidden_dim, out_dim)
        self.act = nn.Tanh()

    def forward(self, x):
        x = self.act(self.input_layer(x))
        for block in self.res_blocks:
            x = block(x)
        return self.output_layer(x)


def normalize_columns(df):
    if {"X坐标", "Y坐标", "电压值"}.issubset(df.columns):
        return df.rename(columns={"X坐标": "X", "Y坐标": "Y", "电压值": "V"})[["X", "Y", "V"]]
    lower_map = {col.strip().lower(): col for col in df.columns}
    if {"x", "y", "v"}.issubset(lower_map.keys()):
        return df.rename(columns={lower_map["x"]: "X", lower_map["y"]: "Y", lower_map["v"]: "V"})[["X", "Y", "V"]]
    if df.shape[1] >= 3:
        print("⚠️ 未识别标准列名，使用前 3 列作为 X,Y,V")
        df = df.iloc[:, :3].copy()
        df.columns = ["X", "Y", "V"]
        return df
    raise ValueError(f"无法识别 CSV 列名: {list(df.columns)}")


def load_model(model_path, device):
    # 直接使用 torch.load 进行加载；移除对不存在的
    # `torch.serialization.add_safe_globals` 的调用以兼容当前环境
        checkpoint = torch.load(model_path, map_location=device)

        # 寻找包含 model state_dict 的键
        if isinstance(checkpoint, dict):
            if 'model' in checkpoint:
                state_dict = checkpoint['model']
            elif 'state_dict' in checkpoint:
                state_dict = checkpoint['state_dict']
            elif 'model_state_dict' in checkpoint:
                state_dict = checkpoint['model_state_dict']
            else:
                keys = list(checkpoint.keys())
                if any(isinstance(k, str) and ('input_layer' in k or 'res_blocks' in k) for k in keys):
                    state_dict = checkpoint
                else:
                    raise ValueError(f"无法在 checkpoint 中识别模型权重，找到的键示例: {keys[:10]}")
        else:
            raise ValueError("checkpoint 格式不被支持：不是 dict 类型")

        # 去掉可能的分布式前缀（例如 'module.'）
        def _strip_prefix(sd, prefix='module.'):
            if any(k.startswith(prefix) for k in sd.keys()):
                return { (k[len(prefix):] if k.startswith(prefix) else k): v for k, v in sd.items() }
            return sd

        state_dict = _strip_prefix(state_dict, prefix='module.')

        # 尝试提取 hidden_dim
        if 'input_layer.weight' in state_dict:
            hidden_dim = state_dict['input_layer.weight'].shape[0]
        else:
            matched = [k for k in state_dict.keys() if k.endswith('input_layer.weight')]
            if matched:
                hidden_dim = state_dict[matched[0]].shape[0]
            else:
                # 根据形状推断：寻找第一个二维权重其输入维为 2（X,Y）
                hidden_dim = None
                for k, v in state_dict.items():
                    try:
                        if hasattr(v, 'ndim') and v.ndim == 2 and v.shape[1] == 2:
                            hidden_dim = v.shape[0]
                            print(f"⚠️ 通过参数 '{k}' 的形状推断 hidden_dim={hidden_dim}")
                            break
                    except Exception:
                        continue
                if hidden_dim is None:
                    # 作为后备，使用默认值
                    hidden_dim = 128
                    print('⚠️ 无法推断 hidden_dim，使用默认 hidden_dim=128')

        # 识别残差块数量（查找 res_blocks.<i>. 的索引）
        import re
        indices = set()
        for k in state_dict.keys():
            m = re.search(r'res_blocks\.(\d+)\.', k)
            if m:
                indices.add(int(m.group(1)))
        num_res_blocks = (max(indices) + 1) if indices else 6

        print(f"✅ 自动识别模型：hidden_dim={hidden_dim}, 残差块={num_res_blocks}")
        model = AdaptivePINN(
            input_dim=2,
            hidden_dim=hidden_dim,
            out_dim=1,
            num_res_blocks=num_res_blocks
        ).to(device)

        # 尝试按形状匹配原 checkpoint 的参数到当前模型参数（宽松加载）
        model_sd = model.state_dict()
        mapped = {}
        used_keys = set()
        for target_key, target_val in model_sd.items():
            for k, v in state_dict.items():
                if k in used_keys:
                    continue
                try:
                    if hasattr(v, 'shape') and tuple(v.shape) == tuple(target_val.shape):
                        mapped[target_key] = v
                        used_keys.add(k)
                        break
                except Exception:
                    continue

        # 报告映射情况
        filled = len(mapped)
        total = len(model_sd)
        print(f"ℹ️ 匹配到 {filled}/{total} 个模型参数 (按形状匹配)")

        # 使用部分映射来加载参数（strict=False）
        model_sd.update(mapped)
        model.load_state_dict(model_sd, strict=False)

        scaler_x = checkpoint.get('scaler_x')
        scaler_y = checkpoint.get('scaler_y')
        if scaler_x is None or scaler_y is None:
            print('⚠️ 在 checkpoint 中未找到 scaler_x 或 scaler_y，返回 None（如果需要，请手动提供）')

        model.eval()
        return model, scaler_x, scaler_y


def predict_on_plane(model, scaler_x, scaler_y, X, device):
    X_norm = scaler_x.transform(X)
    with torch.no_grad():
        X_tensor = torch.tensor(X_norm, dtype=torch.float32).to(device)
        y_pred_norm = model(X_tensor).cpu().numpy()
    y_pred = scaler_y.inverse_transform(y_pred_norm).flatten()
    return y_pred


def plot_prediction(df, y_pred, output_path, elev=28.0, azim=-55.0):
    X = df["X"].values
    Y = df["Y"].values
    V_true = df["V"].values

    cmap = "turbo"
    vmin = float(np.percentile(np.concatenate([V_true, y_pred]), 2))
    vmax = float(np.percentile(np.concatenate([V_true, y_pred]), 98))
    if vmin >= vmax:
        vmin = float(min(V_true.min(), y_pred.min()))
        vmax = float(max(V_true.max(), y_pred.max()))

    def draw_filled_plot(x, y, values, title, filename, colorbar_label, vmin=None, vmax=None):
        plt.figure(figsize=(12, 10))
        try:
            x_unique = np.unique(x)
            y_unique = np.unique(y)
            if x_unique.size * y_unique.size == len(x):
                order = np.lexsort((x, y))
                values_sorted = values[order]
                zi = values_sorted.reshape((y_unique.size, x_unique.size))
                xg, yg = np.meshgrid(x_unique, y_unique)
                plt.pcolormesh(xg, yg, zi, cmap=cmap, shading='auto', vmin=vmin, vmax=vmax)
            else:
                plt.scatter(x, y, c=values, cmap=cmap, s=28, vmin=vmin, vmax=vmax, edgecolors='none')
        except Exception:
            plt.scatter(x, y, c=values, cmap=cmap, s=28, vmin=vmin, vmax=vmax, edgecolors='none')
        plt.colorbar(label=colorbar_label)
        plt.xlabel("X")
        plt.ylabel("Y")
        plt.title(title)
        plt.axis('equal')
        plt.tight_layout()
        plt.savefig(filename, dpi=300)
        plt.close()

    draw_filled_plot(X, Y, y_pred, "自适应PINN 预测电压分布", output_path, "Predicted Voltage (V)", vmin, vmax)
    true_output_path = os.path.join(os.path.dirname(output_path), "pinn_prediction_true.png")
    draw_filled_plot(X, Y, V_true, "真实电压分布", true_output_path, "True Voltage (V)", vmin, vmax)

    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection='3d')
    ax.scatter(X, Y, V_true, color='#1f77b4', s=28, alpha=0.8, label='True')
    ax.scatter(X, Y, y_pred, color='#d62728', s=24, alpha=0.6, label='Predicted')
    ax.set_title("3D 真实值 vs 预测值对比")
    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.set_zlabel("Voltage")
    ax.view_init(elev=elev, azim=azim)
    ax.legend()
    plt.tight_layout()
    plt.show()
    fig.savefig(os.path.join(os.path.dirname(output_path), "pinn_prediction_scatter.png"), dpi=300, bbox_inches='tight')
    plt.close()

    fig, axs = plt.subplots(2, 2, figsize=(16, 12))
    residuals = y_pred - V_true
    axs[0,0].scatter(V_true, y_pred, c=residuals, cmap="coolwarm", s=20)
    axs[0,0].plot([V_true.min(), V_true.max()], [V_true.min(), V_true.max()], 'k--')
    axs[0,0].set_title("真实值 vs 预测值")
    axs[0,1].hist(residuals, bins=50, color='gray')
    axs[0,1].set_title("误差分布")
    axs[1,0].scatter(X, Y, c=V_true, cmap=cmap, s=30)
    axs[1,0].set_title("真实空间分布")
    axs[1,1].scatter(X, Y, c=y_pred, cmap=cmap, s=30)
    axs[1,1].set_title("预测空间分布")
    plt.tight_layout()
    plt.savefig(os.path.join(os.path.dirname(output_path), "pinn_prediction_error.png"), dpi=300)
    plt.close()


def main():
    args = parse_args()
    output_dir = os.path.abspath(args.output_dir)
    os.makedirs(output_dir, exist_ok=True)
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    csv_file = resolve_input_csv(args.data_dir, args.csv)
    print(f"脚本目录：{SCRIPT_DIR}")
    print(f"使用数据：{csv_file}")

    df = pd.read_csv(csv_file, encoding="utf-8-sig")
    df = normalize_columns(df)
    print(f"数据点数：{len(df)} | X[{df.X.min():.1f},{df.X.max():.1f}] Y[{df.Y.min():.1f},{df.Y.max():.1f}]")

    model_path = resolve_model_path(args.model)
    print(f"使用模型：{model_path}")

    model, scaler_x, scaler_y = load_model(model_path, device)
    X = df[["X", "Y"]].values
    y_pred = predict_on_plane(model, scaler_x, scaler_y, X, device)

    mse = np.mean((y_pred - df.V)**2)
    mae = np.mean(np.abs(y_pred - df.V))
    print(f"✅ 预测完成 | RMSE={np.sqrt(mse):.4f} | MAE={mae:.4f}")

    plot_prediction(df, y_pred, os.path.join(output_dir, OUTPUT_IMAGE), elev=args.elev, azim=args.azim)
    print(f"✅ 图片已保存到：{output_dir}")


if __name__ == "__main__":
    main()