#!/usr/bin/env python3
"""
测试新的 RAG 服务。
"""

import os
import sys
import tempfile
from pathlib import Path

# 添加项目根目录到 Python 路径
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

from app.services.rag import create_embedding_service
from app.core.util import load_env_file

def test_rag_service():
    """测试 RAG 服务的基本功能。"""
    print("🔧 测试基于 Quivr Core 的 RAG 服务...")
    
    # 加载环境变量
    load_env_file()
    
    # 检查必需的环境变量
    required_env_vars = [
        'DASHSCOPE_API_KEY',
        'DASHSCOPE_BASE_URL'
    ]
    
    missing_vars = [var for var in required_env_vars if not os.getenv(var)]
    if missing_vars:
        print(f"❌ 缺少环境变量: {', '.join(missing_vars)}")
        print("请确保 .env 文件包含所有必需的配置")
        return False
    
    try:
        # 创建 RAG 服务
        print("\n📝 创建 RAG 服务...")
        service = create_embedding_service(user_id="test_user")
        
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
            print(f"\n查询: {query}")
            results = service.search_documents(query, limit=3)
            
            if results:
                for i, result in enumerate(results, 1):
                    print(f"  结果 {i}:")
                    print(f"    内容: {result['content'][:100]}...")
                    print(f"    分数: {result.get('score', 'N/A')}")
            else:
                print("  无搜索结果")
        
        # 测试统计信息
        print("\n📊 获取统计信息...")
        stats = service.get_user_stats()
        print(f"✅ 用户统计:")
        print(f"   用户ID: {stats['user_id']}")
        print(f"   文档总数: {stats['total_documents']}")
        print(f"   分块总数: {stats['total_chunks']}")
        print(f"   框架: {stats['framework']}")
        
        # 清理
        service.close()
        test_file_path.unlink()
        
        print("\n🎉 RAG 服务测试完成！")
        return True
        
    except Exception as e:
        print(f"❌ 测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False

def test_api_compatibility():
    """测试 API 兼容性。"""
    print("\n🔗 测试 API 兼容性...")
    
    try:
        # 测试导入
        from app.api.v1.files import process_document_background
        print("✅ API 导入成功")
        
        # 测试工厂函数
        service = create_embedding_service(
            user_id="compatibility_test",
            use_advanced=True,
            use_fastembed=False,
            use_rerank=True
        )
        
        print("✅ 工厂函数兼容")
        
        # 测试方法存在性
        methods = ['process_document', 'search_documents', 'delete_document', 'get_user_stats', 'close']
        for method in methods:
            if hasattr(service, method):
                print(f"✅ 方法 {method} 存在")
            else:
                print(f"❌ 方法 {method} 不存在")
                return False
        
        service.close()
        return True
        
    except Exception as e:
        print(f"❌ API 兼容性测试失败: {e}")
        return False

if __name__ == "__main__":
    print("🚀 开始测试新的 RAG 服务...")
    
    # 测试基本功能
    basic_test_passed = test_rag_service()
    
    # 测试 API 兼容性
    compatibility_test_passed = test_api_compatibility()
    
    # 总结
    print("\n" + "="*50)
    print("📋 测试总结:")
    print(f"   基本功能测试: {'✅ 通过' if basic_test_passed else '❌ 失败'}")
    print(f"   API 兼容性测试: {'✅ 通过' if compatibility_test_passed else '❌ 失败'}")
    
    if basic_test_passed and compatibility_test_passed:
        print("\n🎉 所有测试通过！新的 RAG 服务已准备就绪。")
        sys.exit(0)
    else:
        print("\n⚠️  部分测试失败，请检查配置和依赖。")
        sys.exit(1)
