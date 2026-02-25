"""
基于 Quivr Core 的 RAG 服务。

使用 Quivr 的完整 RAG 解决方案。
"""

import logging
import os
import time
import warnings
from pathlib import Path
from typing import List, Dict, Any

# 抑制 Pydantic V1/V2 混合警告
warnings.filterwarnings("ignore", message=".*Mixing V1 models and V2 models.*")

from quivr_core import Brain
from dotenv import load_dotenv

logger = logging.getLogger(__name__)


class RAGService:
    """基于 Quivr Core 的 RAG 服务。"""

    def __init__(self, user_id: str):
        """初始化 RAG 服务。"""
        # 重新加载环境变量
        load_dotenv(override=True)
        
        self.user_id = user_id
        self.brain_name = f"brain_{self.user_id}"
        self.brain = None
        self.processed_files = []

        logger.info("初始化 Quivr RAG 服务，用户: %s", user_id)

    def _setup_environment(self):
        """设置环境变量。"""
        load_dotenv(override=True)
        
        dashscope_key = os.getenv('DASHSCOPE_API_KEY', '')
        dashscope_url = os.getenv('DASHSCOPE_BASE_URL', '')
        
        if not dashscope_key or not dashscope_url:
            raise ValueError("DASHSCOPE_API_KEY 和 DASHSCOPE_BASE_URL 必须设置")
        
        os.environ["OPENAI_API_KEY"] = dashscope_key
        os.environ["OPENAI_BASE_URL"] = dashscope_url
        
        logger.info("使用 DashScope API: %s", dashscope_url)

    def process_document(self, doc_id: str, file_path: str) -> Dict[str, Any]:
        """处理文档。"""
        start_time = time.time()

        try:
            if not Path(file_path).exists():
                raise FileNotFoundError(f"文件不存在: {file_path}")

            logger.info("处理文档: %s", file_path)

            if file_path not in self.processed_files:
                self.processed_files.append(file_path)

            # 设置环境变量
            self._setup_environment()

            # 创建或更新 Brain
            self.brain = Brain.from_files(
                name=self.brain_name,
                file_paths=self.processed_files,
            )

            processing_time = time.time() - start_time
            file_size = Path(file_path).stat().st_size
            estimated_chunks = max(1, file_size // 1000)

            return {
                "status": "completed",
                "doc_id": doc_id,
                "chunks_count": estimated_chunks,
                "processing_time": processing_time,
            }

        except Exception as e:
            logger.error("文档处理失败 %s: %s", doc_id, e)
            return {
                "status": "failed",
                "error": str(e),
                "processing_time": time.time() - start_time,
            }

    def search_documents(self, query: str, limit: int = 5) -> List[Dict[str, Any]]:
        """搜索文档 - 直接使用 Quivr 的 ask 方法。"""
        try:
            if self.brain is None:
                logger.warning("Brain 未初始化")
                return []

            logger.info("搜索查询: %s", query)
            
            # 直接使用 Quivr 的 ask 方法
            answer = self.brain.ask(query)

            return [{
                "id": "result_0",
                "content": answer.answer,
                "metadata": {
                    "user_id": self.user_id,
                    "query": query,
                    "source": "quivr_rag"
                },
                "score": 0.9,
            }]

        except Exception as e:
            logger.error("搜索失败: %s", e)
            return []

    def delete_document(self, doc_id: str) -> int:
        """删除文档。"""
        try:
            logger.info("删除文档 %s", doc_id)
            
            if self.brain is not None:
                self.brain = None
                self.processed_files.clear()
                return 1
            
            return 0
            
        except Exception as e:
            logger.error("删除文档失败 %s: %s", doc_id, e)
            return 0

    def get_user_stats(self) -> Dict[str, Any]:
        """获取用户统计信息。"""
        return {
            "user_id": self.user_id,
            "total_documents": len(self.processed_files),
            "total_chunks": len(self.processed_files),
            "brain_name": self.brain_name,
            "framework": "quivr"
        }

    def close(self):
        """关闭服务。"""
        if self.brain is not None:
            self.brain = None
        self.processed_files.clear()
        logger.info("关闭 Quivr RAG 服务，用户: %s", self.user_id)


def create_embedding_service(user_id: str, **kwargs) -> RAGService:
    """创建 RAG 服务实例。"""
    return RAGService(user_id=user_id)
