from fastapi import APIRouter, UploadFile, File, HTTPException, BackgroundTasks
from fastapi.responses import JSONResponse
from app.models.documents import UploadInitResponse, DocumentMeta, IndexResponse
from app.models.files import FileObject, FileListResponse
from app.services.storage import get_storage
from app.services.embedding import create_embedding_service
import time
import logging

logger = logging.getLogger(__name__)

router = APIRouter()

@router.post("/files/upload", response_model=UploadInitResponse)
async def upload_file(file: UploadFile = File(...)):
    """上传文件"""
    if not file.filename:
        raise HTTPException(status_code=400, detail="文件名不能为空")
    
    storage = get_storage()
    
    # 读取文件内容
    content = await file.read()
    file_size = len(content)
    
    # 初始化上传
    file_id, upload_path = storage.init_upload(
        filename=file.filename,
        mime_type=file.content_type or "application/octet-stream",
        expected_bytes=file_size
    )
    
    # 写入文件内容
    with open(upload_path, "wb") as f:
        f.write(content)
    
    # 完成上传
    stored_file = storage.finalize(file_id)

    return UploadInitResponse(
        file_id=file_id,
        upload_url=upload_path,
        content_type=stored_file.mime_type,
        max_bytes=50 * 1024 * 1024
    )


async def process_document_background(file_id: str, file_path: str, user_id: str = "default"):
    """后台处理文档的函数。"""
    try:
        logger.info("开始后台处理文档: %s", file_id)

        # 创建嵌入服务
        embedding_service = create_embedding_service(
            user_id=user_id,
            use_advanced=True,  # 优先使用增强服务
            use_fastembed=False,  # 避免下载额外模型
            use_rerank=True
        )

        # 处理文档
        result = embedding_service.process_document(file_id, file_path)

        if result["status"] == "completed":
            logger.info("文档处理完成: %s, 分块数: %s", file_id, result['chunks_count'])
        else:
            logger.error("文档处理失败: %s, 错误: %s", file_id, result.get('error'))

        # 关闭服务
        embedding_service.close()

    except Exception as e:
        logger.error("后台文档处理异常: %s, 错误: %s", file_id, e)


@router.post("/files/{file_id}/process", response_model=IndexResponse)
async def process_file(
    file_id: str,
    background_tasks: BackgroundTasks,
    user_id: str = "default"
):
    """处理文件并进行向量化"""
    storage = get_storage()

    # 检查文件是否存在
    file_path = storage.get_file_path(file_id)
    if not file_path:
        raise HTTPException(status_code=404, detail="文件未找到")

    # 添加后台任务
    background_tasks.add_task(
        process_document_background,
        file_id=file_id,
        file_path=file_path,
        user_id=user_id
    )

    return IndexResponse(
        doc_id=file_id,
        doc_version_id=file_id,
        chunks_indexed=0  # 后台处理，暂时返回0
    )

@router.get("/files", response_model=FileListResponse)
async def list_files():
    """获取文件列表"""
    storage = get_storage()
    
    # 简单实现：扫描uploads目录
    files = []
    if hasattr(storage, 'root') and storage.root.exists():
        for file_path in storage.root.glob("*__*"):
            if file_path.is_file():
                stat = file_path.stat()
                file_id = file_path.name.split("__")[0]
                filename = file_path.name.split("__", 1)[1]
                
                files.append(FileObject(
                    id=file_id,
                    filename=filename,
                    bytes=stat.st_size,
                    created_at=int(stat.st_ctime),
                    purpose="assistants"
                ))
    
    return FileListResponse(data=files)

@router.get("/files/{file_id}")
async def get_file_info(file_id: str):
    """获取文件信息"""
    storage = get_storage()
    
    try:
        stored_file = storage.finalize(file_id)
        return DocumentMeta(
            doc_id=file_id,
            latest_version_id=file_id,
            filename=stored_file.filename,
            mime_type=stored_file.mime_type,
            size_bytes=stored_file.bytes
        )
    except FileNotFoundError:
        raise HTTPException(status_code=404, detail="文件未找到")

@router.delete("/files/{file_id}")
async def delete_file(file_id: str):
    """删除文件"""
    storage = get_storage()
    
    if hasattr(storage, 'root'):
        matches = list(storage.root.glob(f"{file_id}__*"))
        if not matches:
            raise HTTPException(status_code=404, detail="文件未找到")
        
        matches[0].unlink()
        return {"message": "文件已删除"}
    else:
        raise HTTPException(status_code=500, detail="不支持删除操作")


@router.post("/files/search")
async def search_documents(
    query: str,
    user_id: str = "default",
    limit: int = 5
):
    """搜索文档内容"""
    try:
        # 创建嵌入服务
        embedding_service = create_embedding_service(
            user_id=user_id,
            use_advanced=True,
            use_fastembed=False,
            use_rerank=True
        )

        # 执行搜索
        results = embedding_service.search_documents(
            query=query,
            limit=limit
        )

        # 关闭服务
        embedding_service.close()

        return {
            "query": query,
            "results": results,
            "total": len(results)
        }

    except Exception as e:
        logger.error("搜索失败: %s", e)
        raise HTTPException(status_code=500, detail=f"搜索失败: {str(e)}")


@router.get("/files/{file_id}/stats")
async def get_file_stats(file_id: str, user_id: str = "default"):
    """获取文件的处理统计信息"""
    try:
        # 创建嵌入服务
        embedding_service = create_embedding_service(
            user_id=user_id,
            use_advanced=True,
            use_fastembed=False,
            use_rerank=True
        )

        # 获取统计信息
        stats = embedding_service.get_user_stats()

        # 关闭服务
        embedding_service.close()

        return {
            "file_id": file_id,
            "user_stats": stats
        }

    except Exception as e:
        logger.error("获取统计信息失败: %s", e)
        raise HTTPException(status_code=500, detail=f"获取统计信息失败: {str(e)}")
