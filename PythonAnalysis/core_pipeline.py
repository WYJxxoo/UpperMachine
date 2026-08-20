# core_pipeline.py
import os
import glob
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from scipy.ndimage import median_filter, laplace, binary_dilation
from scipy.interpolate import RegularGridInterpolator
from scipy.optimize import curve_fit
from matplotlib import rcParams
import psutil
import warnings

warnings.filterwarnings('ignore')

# 配置字体，防止中文乱码
rcParams['font.sans-serif'] = ['SimHei', 'Microsoft YaHei', 'PingFang SC', 'DejaVu Sans']
rcParams['axes.unicode_minus'] = False
rcParams['mathtext.fontset'] = 'dejavusans'

def detect_outliers_iqr(data, multiplier=3):
    Q1, Q3 = np.percentile(data, 25), np.percentile(data, 75)
    IQR = Q3 - Q1
    return (data < Q1 - multiplier * IQR) | (data > Q3 + multiplier * IQR)

def identify_electrode_region(
    X_data,
    Y_data,
    voltage_data,
    threshold_range=0.1,
    neighbor_count_threshold=2,
    voltage_similarity_range=0.09,
    electrode_spatial_tolerance=0.06,
    electrode_expand_steps=1,
):
    voltage_max, voltage_min = np.max(voltage_data), np.min(voltage_data)
    initial_mask = (voltage_data >= voltage_max - threshold_range) | (voltage_data <= voltage_min + threshold_range)
    
    electrode_mask = np.zeros_like(initial_mask, dtype=bool)
    n_points = len(voltage_data)
    
    for i in range(n_points):
        if not initial_mask[i]:
            continue
        distances = np.sqrt((X_data - X_data[i])**2 + (Y_data - Y_data[i])**2)
        neighbor_indices = np.argsort(distances)[1:min(11, n_points)]
        
        similar_count = sum(1 for idx in neighbor_indices if abs(voltage_data[idx] - voltage_data[i]) <= voltage_similarity_range)
        if similar_count >= neighbor_count_threshold:
            electrode_mask[i] = True

    # 考虑电极测量误差：把电极附近“应属于电极但电压略有偏差”的点也纳入剔除范围
    if np.any(electrode_mask):
        electrode_indices = np.where(electrode_mask)[0]
        for i in electrode_indices:
            nearby_same_voltage = (
                np.abs(voltage_data - voltage_data[i]) <= voltage_similarity_range
            ) & (
                np.sqrt((X_data - X_data[i])**2 + (Y_data - Y_data[i])**2) <= electrode_spatial_tolerance
            )
            electrode_mask |= nearby_same_voltage

        # 再做一轮空间扩张，覆盖电极边界抖动和采样偏差
        for _ in range(max(0, int(electrode_expand_steps))):
            expanded_mask = electrode_mask.copy()
            for i in np.where(electrode_mask)[0]:
                nearby = np.sqrt((X_data - X_data[i])**2 + (Y_data - Y_data[i])**2) <= electrode_spatial_tolerance
                expanded_mask |= nearby
            electrode_mask = expanded_mask
            
    return electrode_mask, voltage_max, voltage_min

def fit_surface_2d(XY, a, b, c, d, e, f):
    x, y = XY
    return a * x**2 + b * y**2 + c * x * y + d * x + e * y + f

def run_analysis(algo_name, spatial_anomaly_detector):
    """
    核心执行管线
    :param algo_name: 算法名称（如 '孤立森林', 'LOF'），用于命名前缀
    :param spatial_anomaly_detector: 异常检测函数，接收 (N, 3) 特征矩阵，返回布尔数组 (N,)
    """
    print(f"\n{'='*60}\n🚀 正在使用算法：【{algo_name}】 进行数据处理\n{'='*60}")
    
    # 1. 自动读取数据
    base_dir = os.path.dirname(os.path.abspath(__file__))
    file_pattern = os.path.join(base_dir, "..", "数据", "*.csv")
    csv_files = sorted(glob.glob(file_pattern), key=os.path.getmtime, reverse=True)
    if not csv_files: raise FileNotFoundError(f"❌ 未找到点位数据CSV文件！路径：{file_pattern}")
    
    df = pd.read_csv(csv_files[0]).rename(columns={"X坐标": "X", "Y坐标": "Y", "电压值": "Voltage"})
    if df.duplicated(subset=['X', 'Y']).sum() > 0: df = df.drop_duplicates(subset=['X', 'Y'], keep='last')
    df = df.dropna()

    # 2. 全局电压异常移除
    v_outliers = detect_outliers_iqr(df['Voltage'].values)
    df = df[~v_outliers]

    X_original, Y_original, Z_voltage = df['X'].values, df['Y'].values, df['Voltage'].values
    n_points = len(df)

    # 3. 识别电极区域
    electrode_points, v_max, v_min = identify_electrode_region(X_original, Y_original, Z_voltage)
    non_electrode_mask = ~electrode_points
    
    # 4. 算法注入：孤立异常数据识别
    isolated_outliers = np.zeros(n_points, dtype=bool)
    if np.sum(non_electrode_mask) > 20:
        v_anomaly_mask = Z_voltage <= 0.01  # 低电压直接视为异常
        isolated_outliers[non_electrode_mask] |= v_anomaly_mask[non_electrode_mask]
        
        non_v_mask = ~v_anomaly_mask
        valid_spatial_mask = non_electrode_mask & non_v_mask
        
        if np.sum(valid_spatial_mask) > 20:
            features = np.column_stack([X_original[valid_spatial_mask], Y_original[valid_spatial_mask], Z_voltage[valid_spatial_mask]])
            # 调用外部传入的检测算法
            spatial_anomaly_mask = spatial_anomaly_detector(features)
            isolated_outliers[valid_spatial_mask] |= spatial_anomaly_mask

    n_normal = np.sum(~(electrode_points | isolated_outliers))
    n_electrode = np.sum(electrode_points)
    n_isolated = np.sum(isolated_outliers)

    # 5. 数据网格化与去噪
    df_pivot = df.pivot(index="Y", columns="X", values="Voltage")
    X_unique, Y_unique, Z_raw = df_pivot.columns.values, df_pivot.index.values, df_pivot.values

    if np.isnan(Z_raw).any():
        from scipy.interpolate import griddata
        y_coords, x_coords = np.meshgrid(Y_unique, X_unique, indexing='ij')
        valid_mask = ~np.isnan(Z_raw)
        pts, vals = np.column_stack((x_coords[valid_mask], y_coords[valid_mask])), Z_raw[valid_mask]
        grid_pts = np.column_stack((x_coords.ravel(), y_coords.ravel()))
        Z_raw = griddata(pts, vals, grid_pts, method='linear').reshape(Z_raw.shape)
        if np.isnan(Z_raw).any(): Z_raw = griddata(pts, vals, grid_pts, method='nearest').reshape(Z_raw.shape)

    Z_cleaned = median_filter(Z_raw, size=5)
    
    # 映射二维网格的剔除区域
    df_copy = df.copy()
    df_copy['Exclude'] = electrode_points | isolated_outliers
    exclude_pivot = df_copy.pivot(index="Y", columns="X", values="Exclude").values
    remove_mask = np.nan_to_num(exclude_pivot, nan=False).astype(bool)
    keep_mask = ~remove_mask

    # ================= 新增：电场矢量计算 =================
    dy = np.mean(np.diff(Y_unique))
    dx = np.mean(np.diff(X_unique))
    Ey, Ex = np.gradient(-Z_cleaned, dy, dx)

    # 归一化电场矢量
    E_mag = np.sqrt(Ex**2 + Ey**2) + 1e-10
    Ex_norm, Ey_norm = Ex / E_mag, Ey / E_mag

    # 生成网格并降采样箭头
    x_mesh, y_mesh = np.meshgrid(X_unique, Y_unique)
    skip = 3  
    x_arrow, y_arrow = x_mesh[::skip, ::skip], y_mesh[::skip, ::skip]
    Ex_arrow, Ey_arrow = Ex_norm[::skip, ::skip], Ey_norm[::skip, ::skip]
    
    # 获取对应降采样位置的保留区域掩码，只在非剔除区域画箭头
    mask_arrow = keep_mask[::skip, ::skip]
    x_arrow, y_arrow = x_arrow[mask_arrow], y_arrow[mask_arrow]
    Ex_arrow, Ey_arrow = Ex_arrow[mask_arrow], Ey_arrow[mask_arrow]
    # ===================================================

    # 6. 计算拉普拉斯与统计值
    Z_laplace_full = laplace(Z_cleaned)
    Z_laplace = Z_laplace_full.copy()
    
    # 膨胀剔除区域1圈，彻底消除剔除区域边缘导致的卷积“假性悬崖”污染
    expanded_remove_mask = binary_dilation(remove_mask, iterations=1)
    Z_laplace[expanded_remove_mask] = np.nan

    volt_mean = np.round(np.mean(Z_cleaned[keep_mask]), 8)
    lap_mean = np.round(np.nanmean(Z_laplace), 8)
    lap_std = np.round(np.nanstd(Z_laplace), 8)

    # 7. 插值绘图准备
    interp_res = 200 if psutil.virtual_memory().available / (1024**3) < 2 else 500
    xi, yi = np.linspace(X_unique.min(), X_unique.max(), interp_res), np.linspace(Y_unique.min(), Y_unique.max(), interp_res)
    xi_grid, yi_grid = np.meshgrid(xi, yi)
    
    pts_interp = np.array([yi_grid.ravel(), xi_grid.ravel()]).T
    
    zi = RegularGridInterpolator((Y_unique, X_unique), Z_cleaned, bounds_error=False)(pts_interp).reshape(interp_res, interp_res)
    zi_lap = RegularGridInterpolator((Y_unique, X_unique), Z_laplace_full, bounds_error=False)(pts_interp).reshape(interp_res, interp_res)
    mask_interp = RegularGridInterpolator((Y_unique, X_unique), remove_mask.astype(float), bounds_error=False, fill_value=0)(pts_interp).reshape(interp_res, interp_res)

    # 精准挖空（将无效区域设为NaN透出底色）
    zi_lap[mask_interp >= 0.5] = np.nan

    # 创建保存目录
    save_dir = os.path.join(base_dir, "..", "图片", "数据处理")
    os.makedirs(save_dir, exist_ok=True)
    
    # --- 绘图 1: 电压分布 + 电场矢量 ---
    fig1, ax1 = plt.subplots(figsize=(11, 9), dpi=150)
    cf1 = ax1.contourf(xi_grid, yi_grid, zi, 100, cmap="jet", alpha=0.85)
    plt.colorbar(cf1, ax=ax1, label="电压 (V)")
    
    # 绘制白色电场矢量箭头
    ax1.quiver(x_arrow, y_arrow, Ex_arrow, Ey_arrow, color='white', pivot='middle',
               linewidth=1.2, headwidth=4, headlength=5, alpha=0.9, label='电场方向 (归一化)')

    # 使用青柠绿填充剔除区域，深绿色描边
    ax1.contourf(xi_grid, yi_grid, mask_interp, levels=[0.5, 2.0], colors='limegreen', alpha=0.4)
    ax1.clabel(ax1.contour(xi_grid, yi_grid, mask_interp, levels=[0.5], colors='darkgreen', linewidths=2.5), inline=True, fmt={0.5: '✗ 剔除区域(电极+异常)'})
    
    ax1.legend(loc='lower left', fontsize=11, framealpha=0.9)
    ax1.set_title(f"[{algo_name}] 去噪后电压分布与电场方向", fontsize=16, fontweight='bold')
    plt.savefig(os.path.join(save_dir, f"[{algo_name}]1_电压分布.png"), bbox_inches="tight"); plt.close()

    # --- 绘图 2: 拉普拉斯分布 ---
    fig2, ax2 = plt.subplots(figsize=(11, 9), dpi=150)
    
    lap_max_abs = np.nanmax(np.abs(zi_lap))
    if np.isnan(lap_max_abs) or lap_max_abs == 0: lap_max_abs = 1e-5
    norm = plt.cm.colors.TwoSlopeNorm(vmin=-lap_max_abs, vcenter=0, vmax=lap_max_abs)
    
    cf2 = ax2.contourf(xi_grid, yi_grid, zi_lap, 100, cmap="coolwarm", norm=norm, alpha=0.85)
    plt.colorbar(cf2, ax=ax2, label="拉普拉斯值")
    
    # 在挖空的地方铺上青柠绿覆盖
    ax2.contourf(xi_grid, yi_grid, mask_interp, levels=[0.5, 2.0], colors='limegreen', alpha=0.4)
    ax2.contour(xi_grid, yi_grid, mask_interp, levels=[0.5], colors='darkgreen', linestyles='--', linewidths=1.5, alpha=0.8)
    
    # 统计数据框
    stats_text = f'保留区域统计：\n拉普拉斯均值: {lap_mean}\n拉普拉斯标准差: {lap_std}\n电压均值: {volt_mean} V'
    ax2.text(0.02, 0.98, stats_text, transform=ax2.transAxes, fontsize=10,
             verticalalignment='top', bbox=dict(boxstyle='round', facecolor='white', alpha=0.9, edgecolor='black'))
    
    # 更新图例
    green_patch = mpatches.Patch(facecolor='limegreen', edgecolor='darkgreen', linestyle='--', alpha=0.4, label='剔除区域(电极+异常)')
    ax2.legend(handles=[green_patch], loc='lower right', fontsize=11, framealpha=0.9)

    ax2.set_title(f"[{algo_name}] 拉普拉斯算子分布\n保留区域均值：{lap_mean}", 
                  fontsize=16, fontweight='bold', pad=15)
    ax2.set_xlabel("X 坐标", fontsize=12)
    ax2.set_ylabel("Y 坐标", fontsize=12)
    
    plt.savefig(os.path.join(save_dir, f"[{algo_name}]2_拉普拉斯分布.png"), bbox_inches="tight")
    plt.close()

    # --- 绘图 3: 拉普拉斯分布直方图 ---
    fig3, ax3 = plt.subplots(figsize=(11, 9), dpi=150)
    
    # 仅提取有效的拉普拉斯数据（去除了NaN的值）
    lap_data = Z_laplace[~np.isnan(Z_laplace)].flatten()
    
    ax3.hist(lap_data, bins=50, color="steelblue", alpha=0.7, edgecolor="black", linewidth=0.8)
    ax3.axvline(0, color="red", linestyle="--", linewidth=2.5, label="理论值 0", alpha=0.8)
    ax3.axvline(lap_mean, color="darkgreen", linewidth=2.5, label=f"均值 {lap_mean:.8f}", alpha=0.8)
    
    ax3.set_title(f"[{algo_name}] 拉普拉斯值分布直方图（仅保留区域）", fontsize=16, fontweight='bold', pad=15)
    ax3.set_xlabel("拉普拉斯值", fontsize=12)
    ax3.set_ylabel("频次", fontsize=12)
    ax3.legend(loc='upper right', fontsize=12, framealpha=0.9)
    ax3.grid(alpha=0.3, linestyle='--')
    
    # 详细统计文本
    stats_text_hist = f'有效样本数: {len(lap_data)}\n均值: {lap_mean:.8f}\n标准差: {lap_std:.8f}\n最小值: {np.min(lap_data):.8f}\n最大值: {np.max(lap_data):.8f}'
    ax3.text(0.15, 0.97, stats_text_hist, transform=ax3.transAxes, fontsize=10,
             verticalalignment='top', horizontalalignment='right',
             bbox=dict(boxstyle='round', facecolor='white', alpha=0.85, edgecolor='black'))
             
    plt.savefig(os.path.join(save_dir, f"[{algo_name}]3_拉普拉斯直方图.png"), bbox_inches="tight")
    plt.close()

    # --- 绘图 4: 3D空间散点图 (可交互式弹出) ---
    fig4 = plt.figure(figsize=(12, 9), dpi=150)
    ax4 = fig4.add_subplot(111, projection='3d')
    
    # 绘制三类散点
    if n_normal > 0: 
        ax4.scatter(X_original[~(electrode_points | isolated_outliers)], 
                    Y_original[~(electrode_points | isolated_outliers)], 
                    Z_voltage[~(electrode_points | isolated_outliers)], 
                    c='blue', s=20, alpha=0.6, label=f'正常区域 ({n_normal})')
    if n_electrode > 0: 
        ax4.scatter(X_original[electrode_points], Y_original[electrode_points], 
                    Z_voltage[electrode_points], c='red', s=50, label=f'电极区域 ({n_electrode})')
    if n_isolated > 0: 
        ax4.scatter(X_original[isolated_outliers], Y_original[isolated_outliers], 
                    Z_voltage[isolated_outliers], c='orange', s=80, marker='^', label=f'孤立异常 ({n_isolated})')
    
    ax4.set_title(f'[{algo_name}] 3D空间电压分布\n正常(蓝) | 电极(红) | 异常(橙)', fontsize=14, fontweight='bold')
    ax4.legend()
    ax4.view_init(elev=25, azim=45)
    
    # 保存图片
    save_path_3d = os.path.join(save_dir, f"[{algo_name}]4_3D空间散点图.png")
    plt.savefig(save_path_3d, bbox_inches="tight")
    
    print(f"\n✅ 【{algo_name}】 分析完成！成功检出 {n_isolated} 个异常点。")
    print(f"✅ 全部图表均已保存至 图片/数据处理/ 目录。")
    print(f"💡 正在弹出交互式 3D 散点图... (左键拖动旋转视角，右键拖动/滚轮缩放)")
    print(f"⏳ 注：关闭弹出的图像窗口后，程序将彻底结束或进入下一轮算法。")

    # 呼出交互式窗口（会阻塞进程直到你关闭窗口）
    plt.show()