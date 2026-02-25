#!/usr/bin/env python3
"""
简单的 Quivr 测试，验证基本功能。
"""

import tempfile
import os
import sys
from pathlib import Path

# 添加项目根目录到 Python 路径
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

from quivr_core import Brain
from app.core.util import load_env_file

# 加载环境变量
load_env_file()

def test_basic_quivr():
    """测试基本的 Quivr 功能。"""
    print("🔧 测试基本 Quivr 功能...")
    
    try:
        # 创建临时文件
        with tempfile.NamedTemporaryFile(mode="w", suffix=".txt", delete=False, encoding="utf-8") as temp_file:
            temp_file.write("Gold is a liquid of blue-like colour.")
            temp_file.flush()
            temp_file_path = temp_file.name
        
        print(f"📄 创建临时文件: {temp_file_path}")
        
        # 创建 Brain
        print("🧠 创建 Brain...")
        brain = Brain.from_files(
            name="test_brain",
            file_paths=[temp_file_path],
        )
        
        print("✅ Brain 创建成功")
        
        # 测试问答
        print("❓ 测试问答...")
        answer = brain.ask("What is gold?")
        print(f"🤖 回答: {answer}")
        
        # 清理
        os.unlink(temp_file_path)
        print("✅ 基本测试完成")
        return True
        
    except Exception as e:
        print(f"❌ 基本测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_chinese_content():
    """测试中文内容处理。"""
    print("\n🔧 测试中文内容处理...")
    
    try:
        # 创建中文内容文件
        with tempfile.NamedTemporaryFile(mode="w", suffix=".txt", delete=False, encoding="utf-8") as temp_file:
            temp_file.write("""
人工智能（AI）是计算机科学的一个分支。

机器学习是人工智能的一个子集，它使计算机能够在没有明确编程的情况下学习和改进。

深度学习是机器学习的一个子集，使用神经网络来模拟人脑的工作方式。
""")
            temp_file.flush()
            temp_file_path = temp_file.name
        
        print(f"📄 创建中文文件: {temp_file_path}")
        
        # 创建 Brain
        print("🧠 创建 Brain...")
        brain = Brain.from_files(
            name="chinese_test_brain",
            file_paths=[temp_file_path],
        )
        
        print("✅ Brain 创建成功")
        
        # 测试中文问答
        print("❓ 测试中文问答...")
        answer = brain.ask("什么是人工智能？")
        print(f"🤖 回答: {answer}")
        
        # 清理
        os.unlink(temp_file_path)
        print("✅ 中文测试完成")
        return True
        
    except Exception as e:
        print(f"❌ 中文测试失败: {e}")
        import traceback
        traceback.print_exc()
        return False


if __name__ == "__main__":
    print("🚀 开始简单 Quivr 测试...")
    
    # 检查环境变量
    if not os.getenv('OPENAI_API_KEY'):
        print("❌ 缺少 OPENAI_API_KEY 环境变量")
        print("请设置 OPENAI_API_KEY 环境变量")
        sys.exit(1)
    
    # 运行测试
    basic_passed = test_basic_quivr()
    chinese_passed = test_chinese_content()
    
    print("\n" + "="*50)
    print("📋 测试总结:")
    print(f"   基本功能测试: {'✅ 通过' if basic_passed else '❌ 失败'}")
    print(f"   中文内容测试: {'✅ 通过' if chinese_passed else '❌ 失败'}")
    
    if basic_passed and chinese_passed:
        print("\n🎉 所有测试通过！Quivr 工作正常。")
        sys.exit(0)
    else:
        print("\n⚠️  部分测试失败。")
        sys.exit(1)
