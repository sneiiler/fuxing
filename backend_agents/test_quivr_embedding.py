#!/usr/bin/env python3
"""
测试新的基于 Quivr 的嵌入服务。
"""

import os
import sys
from pathlib import Path

# 添加项目根目录到 Python 路径
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

from app.services.embedding import EmbeddingService, create_embedding_service
from app.core.util import load_env_file

def test_embedding_service():
    """测试嵌入服务的基本功能。"""
    print("🔧 测试基于 Quivr 的嵌入服务...")
    
    # 加载环境变量
    load_env_file()
    
    # 检查必需的环境变量
    required_env_vars = [
        'OPENAI_API_KEY',  # 或者其他 LLM API 密钥
    ]
    
    missing_vars = [var for var in required_env_vars if not os.getenv(var)]
    if missing_vars:
        print(f"❌ 缺少环境变量: {', '.join(missing_vars)}")
        print("请确保 .env 文件包含所有必需的配置")
        return False
    
    try:
        # 创建嵌入服务
        print("\n📝 创建嵌入服务...")
        service = create_embedding_service(
            user_id="test_user",
            use_advanced=True,
            use_fastembed=True,
            use_rerank=True
        )
        
        # 创建测试文件
        test_file_path = project_root / "test_document.txt"
        with open(test_file_path, "w", encoding="utf-8") as f:
            f.write("""
这是一个测试文档。

## 关于人工智能

人工智能（AI）是计算机科学的一个分支，致力于创建能够执行通常需要人类智能的任务的系统。

### 机器学习
机器学习是人工智能的一个子集，它使计算机能够在没有明确编程的情况下学习和改进。

### 深度学习
深度学习是机器学习的一个子集，使用神经网络来模拟人脑的工作方式。

## 应用领域
- 自然语言处理
- 计算机视觉
- 语音识别
- 推荐系统
""")
        
        print(f"📄 创建测试文件: {test_file_path}")
        
        # 处理文档
        print("\n🔄 处理文档...")
        result = service.process_document("test_doc", str(test_file_path))
        
        print(f"✅ 文档处理结果:")
        print(f"   状态: {result['status']}")
        print(f"   文档ID: {result.get('doc_id', 'N/A')}")
        print(f"   分块数量: {result.get('chunks_count', 'N/A')}")
        print(f"   处理时间: {result.get('processing_time', 'N/A'):.2f}秒")
        
        if result['status'] != 'completed':
            print(f"❌ 文档处理失败: {result.get('error', '未知错误')}")
            return False
        
        # 测试搜索
        print("\n🔍 测试搜索功能...")
        search_queries = [
            "什么是人工智能？",
            "机器学习的定义",
            "深度学习和神经网络",
            "AI的应用领域有哪些？"
        ]
        
        for query in search_queries:
            print(f"\n🔎 查询: {query}")
            search_results = service.search_documents(query, limit=3)
            
            if search_results:
                print(f"   找到 {len(search_results)} 个结果:")
                for i, result in enumerate(search_results, 1):
                    print(f"   {i}. 相似度: {result.get('score', 'N/A')}")
                    content = result.get('content', '')
                    preview = content[:100] + "..." if len(content) > 100 else content
                    print(f"      内容预览: {preview}")
            else:
                print("   ❌ 未找到相关结果")
        
        # 获取统计信息
        print("\n📊 获取统计信息...")
        stats = service.get_user_stats()
        print(f"   用户ID: {stats.get('user_id')}")
        print(f"   文档数量: {stats.get('total_documents')}")
        print(f"   分块数量: {stats.get('total_chunks')}")
        print(f"   框架: {stats.get('framework')}")
        
        # 清理
        service.close()
        test_file_path.unlink()  # 删除测试文件
        
        print("\n✅ 测试完成！")
        return True
        
    except Exception as e:
        print(f"\n❌ 测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_file_upload_integration():
    """测试文件上传集成。"""
    print("\n🔧 测试文件上传集成...")
    
    try:
        from app.services.storage import get_storage
        
        # 测试存储服务
        storage = get_storage()
        print(f"✅ 存储服务初始化成功: {storage}")
        
        # 创建测试文件
        test_content = b"This is a test file for upload integration."
        
        # 初始化上传
        file_id, upload_path = storage.init_upload(
            filename="test_integration.txt",
            mime_type="text/plain",
            expected_bytes=len(test_content)
        )
        
        print(f"📤 初始化上传: {file_id} -> {upload_path}")
        
        # 写入文件
        with open(upload_path, "wb") as f:
            f.write(test_content)
        
        # 完成上传
        stored_file = storage.finalize(file_id)
        print(f"✅ 上传完成: {stored_file.filename} ({stored_file.bytes} bytes)")
        
        # 测试嵌入服务处理
        service = create_embedding_service(
            user_id="integration_test_user",
            use_advanced=True,
            use_fastembed=True,
            use_rerank=True
        )
        result = service.process_document(file_id, stored_file.file_path)
        
        print(f"📝 文档处理结果: {result['status']}")
        
        # 清理
        service.close()
        storage.delete_file(file_id)
        
        print("✅ 文件上传集成测试完成！")
        return True
        
    except Exception as e:
        print(f"❌ 文件上传集成测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


if __name__ == "__main__":
    print("🚀 开始测试基于 Quivr 的 RAG 系统...")
    
    # 测试基本功能
    basic_test_passed = test_embedding_service()
    
    # 测试集成功能
    integration_test_passed = test_file_upload_integration()
    
    # 总结
    print("\n" + "="*50)
    print("📋 测试总结:")
    print(f"   基本功能测试: {'✅ 通过' if basic_test_passed else '❌ 失败'}")
    print(f"   集成功能测试: {'✅ 通过' if integration_test_passed else '❌ 失败'}")
    
    if basic_test_passed and integration_test_passed:
        print("\n🎉 所有测试通过！Quivr RAG 系统已准备就绪。")
        sys.exit(0)
    else:
        print("\n⚠️  部分测试失败，请检查配置和依赖。")
        sys.exit(1)
