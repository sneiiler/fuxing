from fastapi import APIRouter, Request
from fastapi.responses import StreamingResponse, JSONResponse
from datetime import datetime
import json, time
from app.models.chat import ChatCompletionsRequest, ChatCompletionsResponse, Choice, ChatMessage, Usage
from app.models.enums import FinishReason
from app.models.common import DocRef
from app.core.config import settings

router = APIRouter()

@router.post("/chat/completions")
async def chat_completions(req: ChatCompletionsRequest, request: Request):
    """处理聊天补全请求的API端点。
    
    这个函数实现了兼容OpenAI格式的聊天补全API，支持流式和非流式两种响应模式。
    目前为存根实现，返回固定的测试响应，未来将被LangGraph流水线替换。
    
    Args:
        req (ChatCompletionsRequest): 聊天补全请求对象，包含消息历史、模型选择、
            流式模式标志等参数。
        request (Request): FastAPI请求对象，包含HTTP请求的元信息。
        
    Returns:
        JSONResponse | StreamingResponse: 根据请求的stream参数返回不同类型的响应：
            - 非流式模式：返回JSONResponse，包含完整的聊天补全响应
            - 流式模式：返回StreamingResponse，以Server-Sent Events格式
              逐步发送响应块
              
    Note:
        这是一个存根实现，返回固定的"Hello from stub."或"Hello from stream."
        消息。在生产环境中，这个函数应该被替换为实际的AI模型调用。
        
    Example:
        非流式请求将返回完整的ChatCompletionsResponse对象。
        流式请求将返回多个数据块，每个块都是JSON格式的增量更新。
    """
    # Stub: non-stream + stream modes. Replace with LangGraph pipeline.
    if not req.stream:
        now = int(time.time())
        choice = Choice(index=0, message=ChatMessage(role="assistant", content="Hello from stub."), finish_reason=FinishReason.stop)
        resp = ChatCompletionsResponse(
            id=f"chatcmpl_{now}",
            created=now,
            model=req.model,
            choices=[choice],
            usage=Usage(prompt_tokens=0, completion_tokens=0, total_tokens=0),
        )
        return JSONResponse(resp.model_dump())
    else:
        async def event_gen():
            import random
            now = int(time.time())
            completion_id = f"chatcmpl_{now}"
            
            # 获取用户最后一条消息
            user_message = ""
            if req.messages:
                user_message = req.messages[-1].content
            
            # 1. 发送角色信息
            chunk_role = {
                "id": completion_id,
                "object": "chat.completion.chunk",
                "created": now,
                "model": req.model,
                "choices": [{"index": 0, "delta": {"role": "assistant"}, "finish_reason": None}]
            }
            yield f"data: {json.dumps(chunk_role)}\n\n"
            await asyncio_sleep(0.3)  # 稍长的初始延迟
            
            # 2. 模拟思考和确认收到消息
            thinking_response = f"我收到了您的消息：「{user_message}」\n\n让我为您详细解答这个问题..."
            
            # 将响应分词发送，模拟真实的流式效果
            words = thinking_response.split()
            current_content = ""
            
            for i, word in enumerate(words):
                current_content += word
                if i < len(words) - 1:
                    current_content += " "
                
                chunk_content = {
                    "id": completion_id,
                    "object": "chat.completion.chunk",
                    "created": now,
                    "model": req.model,
                    "choices": [{"index": 0, "delta": {"content": word + (" " if i < len(words) - 1 else "")}, "finish_reason": None}]
                }
                yield f"data: {json.dumps(chunk_content)}\n\n"
                
                # 随机延迟，模拟真实的AI生成速度
                delay = random.uniform(0.05, 0.2)
                await asyncio_sleep(delay)
            
            # 3. 添加换行和继续回答
            await asyncio_sleep(0.5)
            
            # 模拟更长的回答
            detailed_response = [
                "\n\n基于您的问题，我可以从以下几个方面来分析：",
                "\n\n1. **技术层面**：这涉及到现代Web开发的最佳实践",
                "\n2. **用户体验**：我们需要考虑用户的实际使用场景",
                "\n3. **性能优化**：确保系统的响应速度和稳定性",
                "\n\n总的来说，这是一个很好的问题，需要综合考虑多个因素。",
                "\n\n如果您需要更详细的说明，请随时告诉我！"
            ]
            
            for section in detailed_response:
                # 将每个部分按字符分块发送
                for char in section:
                    chunk_char = {
                        "id": completion_id,
                        "object": "chat.completion.chunk",
                        "created": now,
                        "model": req.model,
                        "choices": [{"index": 0, "delta": {"content": char}, "finish_reason": None}]
                    }
                    yield f"data: {json.dumps(chunk_char)}\n\n"
                    
                    # 更快的字符级延迟
                    if char in ['\n', '。', '！', '？', '：']:
                        await asyncio_sleep(0.1)  # 标点符号后稍作停顿
                    else:
                        await asyncio_sleep(0.02)
                
                # 段落间停顿
                await asyncio_sleep(0.3)
            
            # 4. 发送结束标记
            await asyncio_sleep(0.2)
            chunk_end = {
                "id": completion_id,
                "object": "chat.completion.chunk",
                "created": now,
                "model": req.model,
                "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]
            }
            yield f"data: {json.dumps(chunk_end)}\n\n"
            yield "data: [DONE]\n\n"
            
        from asyncio import sleep as asyncio_sleep
        return StreamingResponse(event_gen(), media_type="text/event-stream")
