"""异常检测器集合 —— 返回与 core_pipeline.run_analysis 兼容的布尔掩码函数

接口: detector(features: np.ndarray) -> np.ndarray[bool]
features: (N, D) 的输入特征矩阵，返回长度 N 的布尔数组，True 表示异常/需剔除
"""
from typing import Callable, Dict, Any
import numpy as np
from sklearn.ensemble import IsolationForest
from sklearn.neighbors import LocalOutlierFactor
from sklearn.cluster import DBSCAN


def iforest_detector_factory(contamination: float = 0.01, random_state: int = 42) -> Callable:
    def detector(features: np.ndarray) -> np.ndarray:
        # IsolationForest: fit -> predict; -1 表示异常
        clf = IsolationForest(contamination=contamination, random_state=random_state)
        labels = clf.fit_predict(features)
        return labels == -1
    return detector


def lof_detector_factory(n_neighbors: int = 20, contamination: float = 0.01) -> Callable:
    def detector(features: np.ndarray) -> np.ndarray:
        # 使用 LOF 的 fit_predict（返回 -1/1）
        clf = LocalOutlierFactor(n_neighbors=n_neighbors, contamination=contamination)
        labels = clf.fit_predict(features)
        return labels == -1
    return detector


def dbscan_detector_factory(eps: float = 0.05, min_samples: int = 5) -> Callable:
    def detector(features: np.ndarray) -> np.ndarray:
        # 使用 DBSCAN，把标记为噪声的点（label == -1）视为异常
        db = DBSCAN(eps=eps, min_samples=min_samples)
        labels = db.fit_predict(features)
        return labels == -1
    return detector


def get_detector(name: str, params: Dict[str, Any] = None) -> Callable:
    params = params or {}
    name = name.lower()
    if name in ("iforest", "isolationforest", "isolation_forest"):
        return iforest_detector_factory(**params)
    if name in ("lof", "localoutlierfactor"):
        return lof_detector_factory(**params)
    if name in ("dbscan",):
        return dbscan_detector_factory(**params)
    raise ValueError(f"未知的检测器名称: {name}")


if __name__ == '__main__':
    print('anomaly_detectors 模块：提供 iforest/lof/dbscan detector 工厂')
