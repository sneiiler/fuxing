"""
文件存储服务模块。

提供文件上传、存储和管理功能，支持本地文件系统存储。
"""

import os
import time
import uuid
from pathlib import Path
from typing import Optional, Tuple
from dataclasses import dataclass

from app.core.config import settings


@dataclass
class StoredFile:
    """存储的文件信息。"""
    file_id: str
    filename: str
    mime_type: str
    bytes: int
    file_path: str


class LocalFileStorage:
    """本地文件存储服务。"""
    
    def __init__(self, upload_dir: str = "app/uploads"):
        """
        初始化本地文件存储服务。
        
        Args:
            upload_dir: 上传目录路径
        """
        self.root = Path(upload_dir)
        self.root.mkdir(parents=True, exist_ok=True)
    
    def init_upload(
        self, 
        filename: str, 
        mime_type: str, 
        expected_bytes: int
    ) -> Tuple[str, str]:
        """
        初始化文件上传。
        
        Args:
            filename: 文件名
            mime_type: MIME 类型
            expected_bytes: 预期文件大小
            
        Returns:
            (file_id, upload_path) 元组
        """
        # 生成唯一的文件ID
        timestamp = int(time.time() * 1000)
        file_id = f"f_{timestamp}"
        
        # 构建文件路径，格式：file_id__filename
        safe_filename = self._sanitize_filename(filename)
        upload_filename = f"{file_id}__{safe_filename}"
        upload_path = self.root / upload_filename
        
        return file_id, str(upload_path)
    
    def finalize(self, file_id: str) -> StoredFile:
        """
        完成文件上传。
        
        Args:
            file_id: 文件ID
            
        Returns:
            存储的文件信息
            
        Raises:
            FileNotFoundError: 如果文件不存在
        """
        # 查找匹配的文件
        matches = list(self.root.glob(f"{file_id}__*"))
        if not matches:
            raise FileNotFoundError(f"文件不存在: {file_id}")
        
        file_path = matches[0]
        filename = file_path.name.split("__", 1)[1]
        
        # 获取文件信息
        stat = file_path.stat()
        
        # 简单的 MIME 类型检测
        mime_type = self._detect_mime_type(filename)
        
        return StoredFile(
            file_id=file_id,
            filename=filename,
            mime_type=mime_type,
            bytes=stat.st_size,
            file_path=str(file_path)
        )
    
    def get_file_path(self, file_id: str) -> Optional[str]:
        """
        获取文件路径。
        
        Args:
            file_id: 文件ID
            
        Returns:
            文件路径，如果不存在则返回 None
        """
        matches = list(self.root.glob(f"{file_id}__*"))
        if matches:
            return str(matches[0])
        return None
    
    def delete_file(self, file_id: str) -> bool:
        """
        删除文件。
        
        Args:
            file_id: 文件ID
            
        Returns:
            是否删除成功
        """
        matches = list(self.root.glob(f"{file_id}__*"))
        if matches:
            matches[0].unlink()
            return True
        return False
    
    def _sanitize_filename(self, filename: str) -> str:
        """清理文件名，移除不安全字符。"""
        # 简单的文件名清理
        import re
        # 保留字母、数字、点、下划线、连字符
        safe_name = re.sub(r'[^\w\-_\.]', '_', filename)
        return safe_name
    
    def _detect_mime_type(self, filename: str) -> str:
        """根据文件扩展名检测 MIME 类型。"""
        ext = Path(filename).suffix.lower()
        mime_types = {
            '.pdf': 'application/pdf',
            '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            '.doc': 'application/msword',
            '.txt': 'text/plain',
            '.md': 'text/markdown',
            '.html': 'text/html',
            '.htm': 'text/html',
        }
        return mime_types.get(ext, 'application/octet-stream')


# 全局存储实例
_storage_instance = None


def get_storage() -> LocalFileStorage:
    """获取存储服务实例（单例模式）。"""
    global _storage_instance
    if _storage_instance is None:
        _storage_instance = LocalFileStorage()
    return _storage_instance


def set_storage(storage: LocalFileStorage):
    """设置存储服务实例（用于测试）。"""
    global _storage_instance
    _storage_instance = storage
