#!/usr/bin/env python3
"""
测试 Quivr 与自定义配置。
"""

import tempfile
import os
import sys
from pathlib import Path

# 添加项目根目录到 Python 路径
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

from app.core.util import load_env_file

# 加载环境变量
load_env_file()

# 设置环境变量以使用正确的模型
os.environ['OPENAI_EMBEDDING_MODEL'] = 'text-embedding-v4'

from quivr_core import Brain
from langchain_openai import OpenAIEmbeddings, ChatOpenAI

def test_with_custom_embeddings():
    """测试使用自定义嵌入模型的 Quivr。"""
    print("🔧 测试使用自定义嵌入模型的 Quivr...")
    
    try:
        # 创建自定义嵌入模型
        embeddings = OpenAIEmbeddings(
            model="text-embedding-v4",
            api_key=os.getenv('DASHSCOPE_API_KEY'),
            base_url=os.getenv('DASHSCOPE_BASE_URL'),
            check_embedding_ctx_length=False,
        )

        # 创建自定义 LLM
        llm = ChatOpenAI(
            model="qwen-turbo",  # 使用阿里云支持的模型
            api_key=os.getenv('DASHSCOPE_API_KEY'),
            base_url=os.getenv('DASHSCOPE_BASE_URL'),
            temperature=0.7,
        )

        print("✅ 自定义嵌入模型和 LLM 创建成功")
        
        # 创建临时文件
        with tempfile.NamedTemporaryFile(mode="w", suffix=".txt", delete=False, encoding="utf-8") as temp_file:
            temp_file.write("Gold is a precious metal with yellow color.")
            temp_file.flush()
            temp_file_path = temp_file.name
        
        print(f"📄 创建临时文件: {temp_file_path}")
        
        # 创建 Brain 时指定自定义嵌入模型
        print("🧠 创建 Brain...")
        brain = Brain.from_files(
            name="custom_test_brain",
            file_paths=[temp_file_path],
            embedder=embeddings,
        )
        
        print("✅ Brain 创建成功")
        
        # 测试问答
        print("❓ 测试问答...")
        answer = brain.ask("What color is gold?")
        print(f"🤖 回答: {answer}")
        
        # 清理
        os.unlink(temp_file_path)
        print("✅ 自定义嵌入测试完成")
        return True
        
    except Exception as e:
        print(f"❌ 自定义嵌入测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_simple_embedding():
    """测试简单的嵌入功能。"""
    print("\n🔧 测试简单的嵌入功能...")
    
    try:
        # 创建嵌入模型
        embeddings = OpenAIEmbeddings(
            model="text-embedding-v4",
            api_key=os.getenv('DASHSCOPE_API_KEY'),
            base_url=os.getenv('DASHSCOPE_BASE_URL'),
            check_embedding_ctx_length=False,
        )
        
        # 测试嵌入
        print("📝 测试文本嵌入...")
        test_text = "This is a test sentence."
        embedding_result = embeddings.embed_query(test_text)
        
        print(f"✅ 嵌入成功，维度: {len(embedding_result)}")
        print(f"   前5个值: {embedding_result[:5]}")
        
        return True
        
    except Exception as e:
        print(f"❌ 简单嵌入测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


if __name__ == "__main__":
    print("🚀 开始测试 Quivr 自定义配置...")
    
    # 检查环境变量
    required_vars = ['DASHSCOPE_API_KEY', 'DASHSCOPE_BASE_URL']
    missing_vars = [var for var in required_vars if not os.getenv(var)]

    if missing_vars:
        print(f"❌ 缺少环境变量: {', '.join(missing_vars)}")
        sys.exit(1)

    dashscope_key = os.getenv('DASHSCOPE_API_KEY')
    dashscope_url = os.getenv('DASHSCOPE_BASE_URL')
    print(f"🔑 API Key: {dashscope_key[:10] if dashscope_key else 'None'}...")
    print(f"🌐 Base URL: {dashscope_url}")
    
    # 运行测试
    simple_passed = test_simple_embedding()
    custom_passed = test_with_custom_embeddings()
    
    print("\n" + "="*50)
    print("📋 测试总结:")
    print(f"   简单嵌入测试: {'✅ 通过' if simple_passed else '❌ 失败'}")
    print(f"   自定义配置测试: {'✅ 通过' if custom_passed else '❌ 失败'}")
    
    if simple_passed and custom_passed:
        print("\n🎉 所有测试通过！Quivr 自定义配置工作正常。")
        sys.exit(0)
    else:
        print("\n⚠️  部分测试失败。")
        sys.exit(1)
