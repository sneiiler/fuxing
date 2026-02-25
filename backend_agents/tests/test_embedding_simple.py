"""
简单的embedding服务测试 - 测试真实的docx文件处理
"""

import os
import sys
from pathlib import Path

# 添加项目根目录到Python路径
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from app.core.util import get_project_root
from app.services.embedding import EmbeddingService

import logging

# 设置日志模版
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def test_docx_processing():
    """测试处理真实的docx文件"""
    
    # 检查必需的环境变量
    required_env_vars = [
        'EMBEDDING_API_KEY',  # 嵌入服务API密钥
        'SILICONFLOW_API_KEY',  # 重排序服务API密钥
    ]
    
    missing_vars = [var for var in required_env_vars if not os.getenv(var)]
    if missing_vars:
        print(f"❌ 缺少环境变量: {', '.join(missing_vars)}")
        print("请确保 .env 文件包含所有必需的配置")
        return False
    
    # 检查测试文件
    project_root = get_project_root()
    docx_file = project_root / "app" / "uploads" / "f_1756885280844__星座动态分簇大模型申报书（公开）.docx"
    if not docx_file.exists():
        print(f"❌ 测试文件不存在: {docx_file}")
        return False
    
    print(f"📄 测试文件: {docx_file}")
    print(f"📁 文件大小: {docx_file.stat().st_size / 1024:.1f} KB")
    
    try:
        # 创建embedding服务（禁用FastEmbed避免下载模型）
        print("\n🔧 创建embedding服务...")
        service = EmbeddingService("test_user", use_fastembed=False)
        
        # 处理文档
        print("📖 开始处理文档...")
        result = service.process_document("design_report", str(docx_file))
        
        # 输出结果
        print(f"\n✅ 文档处理完成!")
        print(f"   状态: {result['status']}")
        print(f"   文档ID: {result['doc_id']}")
        print(f"   分块数量: {result['chunks_count']}")
        print(f"   处理时间: {result['processing_time']:.2f}秒")
        
        # 测试搜索
        print("\n🔍 测试搜索功能...")
        search_results = service.search_documents("设计", limit=3)
        
        print(f"   找到 {len(search_results)} 个相关结果:")
        for i, result in enumerate(search_results, 1):
            print(f"   {i}. 相似度: {result['score']:.3f}")
            print(f"      内容预览: {result['content'][:100]}...")
            print()
        
        # 获取用户统计
        print("📊 用户统计信息:")
        stats = service.get_user_stats()
        print(f"   用户ID: {stats['user_id']}")
        print(f"   文档总数: {stats['total_documents']}")
        print(f"   分块总数: {stats['total_chunks']}")
        print(f"   Qdrant集合: {stats['collection_name']}")
        
        # 清理测试数据
        print("\n🧹 清理测试数据...")
        deleted_count = service.delete_document("design_report")
        print(f"   删除了 {deleted_count} 个分块")
        
        service.close()
        print("\n🎉 测试完成!")
        return True
        
    except Exception as e:
        print(f"\n❌ 测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_rerank_functionality():
    """测试重排序功能"""
    
    # 检查rerank相关的环境变量
    rerank_env_vars = ['SILICONFLOW_API_KEY']
    missing_vars = [var for var in rerank_env_vars if not os.getenv(var)]
    if missing_vars:
        print(f"❌ 缺少rerank环境变量: {', '.join(missing_vars)}")
        return False
    
    # 检查测试文件
    project_root = get_project_root()
    docx_file = project_root / "app" / "uploads" / "f_1756885280844__星座动态分簇大模型申报书（公开）.docx"
    if not docx_file.exists():
        print(f"❌ 测试文件不存在: {docx_file}")
        return False
    
    try:
        print("\n🔧 创建embedding服务（用于rerank测试）...")
        service = EmbeddingService("test_rerank_user", use_fastembed=False)
        
        # 先处理文档以便有数据可以搜索
        print("📖 处理测试文档...")
        result = service.process_document("rerank_test_doc", str(docx_file))
        print(f"   处理完成: {result['chunks_count']} 个分块")
        
        # 测试普通搜索
        print("\n🔍 测试普通搜索...")
        normal_results = service.search_documents("大模型设计", limit=5)
        print(f"   普通搜索找到 {len(normal_results)} 个结果")
        
        # 测试重排序搜索
        print("\n🎯 测试重排序搜索...")
        rerank_results = service.search_documents("大模型设计", limit=5, use_rerank=True)
        print(f"   重排序搜索找到 {len(rerank_results)} 个结果")
        
        # 比较结果
        print("\n📊 结果对比:")
        print("普通搜索结果:")
        for i, result in enumerate(normal_results[:3], 1):
            print(f"   {i}. 相似度: {result['score']:.3f}")
            print(f"      内容: {result['content'][:80]}...")
        
        print("\n重排序搜索结果:")
        for i, result in enumerate(rerank_results[:3], 1):
            relevance_score = result.get('relevance_score', 'N/A')
            print(f"   {i}. 相似度: {result['score']:.3f}, 重排序分: {relevance_score}")
            print(f"      内容: {result['content'][:80]}...")
        
        # 测试不同的查询
        test_queries = [
            "技术架构",
            "数据处理",
            "模型训练"
        ]
        
        print(f"\n🧪 测试多个查询的重排序效果:")
        for query in test_queries:
            print(f"\n查询: '{query}'")
            try:
                rerank_results = service.search_documents(query, limit=3, use_rerank=True)
                print(f"   找到 {len(rerank_results)} 个重排序结果")
                if rerank_results:
                    best_result = rerank_results[0]
                    relevance_score = best_result.get('relevance_score', 'N/A')
                    print(f"   最佳匹配 - 相似度: {best_result['score']:.3f}, 重排序分: {relevance_score}")
            except Exception as e:
                print(f"   ❌ 查询失败: {e}")
        
        # 清理测试数据
        print("\n🧹 清理rerank测试数据...")
        deleted_count = service.delete_document("rerank_test_doc")
        print(f"   删除了 {deleted_count} 个分块")
        
        service.close()
        print("\n🎉 Rerank测试完成!")
        return True
        
    except Exception as e:
        print(f"\n❌ Rerank测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


if __name__ == "__main__":
    print("=== Embedding服务测试 ===")
    
    # 运行基础文档处理测试
    print("\n📋 1. 基础文档处理测试")
    basic_success = test_docx_processing()
    
    # 运行重排序功能测试
    print("\n📋 2. 重排序功能测试")
    rerank_success = test_rerank_functionality()
    
    # 总结测试结果
    print("\n" + "="*50)
    print("📊 测试结果汇总:")
    print(f"   基础功能测试: {'✅ 通过' if basic_success else '❌ 失败'}")
    print(f"   重排序功能测试: {'✅ 通过' if rerank_success else '❌ 失败'}")
    
    if basic_success and rerank_success:
        print("\n🎉 所有测试通过!")
    else:
        print("\n❌ 部分测试失败!")
        sys.exit(1)
