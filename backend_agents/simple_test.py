#!/usr/bin/env python3
"""简单测试新的 RAG 服务。"""

import sys
from pathlib import Path

# 添加项目根目录到 Python 路径
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

try:
    from app.services.embedding import create_embedding_service
    print("✅ RAG 服务导入成功")

    # 测试创建服务
    service = create_embedding_service('test_user')
    print("✅ RAG 服务创建成功")
    print(f"✅ 服务类型: {type(service).__name__}")
    
    # 测试统计信息
    stats = service.get_user_stats()
    print(f"✅ 统计信息: {stats}")
    
    service.close()
    print("✅ 基本测试完成")

except Exception as e:
    print(f"❌ 测试失败: {e}")
    import traceback
    traceback.print_exc()
