"""
项目工具函数模块。
"""
import os
from pathlib import Path
from typing import Optional


def get_project_root() -> Path:
    """
    获取项目根目录。
    
    策略：从当前文件开始向上查找，直到找到包含 pyproject.toml 的目录。
    
    Returns:
        Path: 项目根目录路径
        
    Raises:
        FileNotFoundError: 如果找不到包含 pyproject.toml 的目录
    """
    current_path = Path(__file__).resolve()
    
    # 从当前文件所在目录开始向上查找
    for parent in [current_path.parent] + list(current_path.parents):
        pyproject_path = parent / "pyproject.toml"
        if pyproject_path.exists():
            return parent
    
    # 如果没找到，抛出异常
    raise FileNotFoundError(
        "无法找到项目根目录：未找到包含 pyproject.toml 的目录"
    )


def load_env_file(env_filename: str = ".env") -> bool:
    """
    加载环境变量文件。
    
    Args:
        env_filename: 环境文件名，默认为 ".env"
        
    Returns:
        bool: 是否成功加载环境文件
    """
    try:
        from dotenv import load_dotenv
        
        project_root = get_project_root()
        env_path = project_root / env_filename
        
        if env_path.exists():
            load_dotenv(env_path)
            return True
        else:
            print(f"警告：环境文件 {env_path} 不存在")
            return False
            
    except ImportError:
        print("警告：python-dotenv 未安装，无法加载环境文件")
        return False
    except Exception as e:
        print(f"加载环境文件时出错：{e}")
        return False
