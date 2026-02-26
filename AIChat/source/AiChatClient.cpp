// -----------------------------------------------------------------------------
// File: AiChatClient.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatClient.hpp"
#include "AiChatPrefObject.hpp"

#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#ifndef QT_NO_SSL
#include <QSslSocket>
#endif
#include <QDateTime>
#include <QRandomGenerator>
#include <QTimer>

namespace AiChat
{

namespace
{
bool ExtractUsage(const QJsonObject& aUsage, int& aPrompt, int& aCompletion, int& aTotal)
{
   aPrompt = 0;
   aCompletion = 0;
   aTotal = 0;

   if (aUsage.isEmpty()) {
      return false;
   }

   const int promptTokens = aUsage.value("prompt_tokens").toInt();
   const int completionTokens = aUsage.value("completion_tokens").toInt();
   const int inputTokens = aUsage.value("input_tokens").toInt();
   const int outputTokens = aUsage.value("output_tokens").toInt();

   aPrompt = (promptTokens > 0) ? promptTokens : inputTokens;
   aCompletion = (completionTokens > 0) ? completionTokens : outputTokens;
   aTotal = aUsage.value("total_tokens").toInt();
   if (aTotal <= 0) {
      aTotal = aPrompt + aCompletion;
   }

   return (aPrompt > 0 || aCompletion > 0 || aTotal > 0);
}
} // namespace


Client::Client(PrefObject* aPrefObject, QObject* aParent)
   : QObject(aParent)
   , mPrefObjectPtr(aPrefObject)
   , mNetworkManager(new QNetworkAccessManager(this))
   , mPendingReply(nullptr)
{
}

Client::~Client()
{
   CancelRequest();
}

void Client::SetSessionId(const QString& aSessionId)
{
   mSessionId = aSessionId;
}

void Client::SendChatRequest(const QList<ChatMessage>& aMessages, const QString& aSystemPrompt)
{
   SendChatRequest(aMessages, aSystemPrompt, QJsonArray());
}

void Client::SendChatRequest(const QList<ChatMessage>& aMessages, const QString& aSystemPrompt, const QJsonArray& aTools)
{
   if (!mPrefObjectPtr || !mPrefObjectPtr->IsConfigured())
   {
      emit ErrorOccurred("Base URL not configured. Please set it in Preferences > AI Chat.");
      return;
   }

   if (mPendingReply)
   {
      if (mStreamCompleted)
      {
         emit DebugEvent(QStringLiteral("Stale pending reply after stream completion - aborting."));
         mPendingReply->abort();
         mPendingReply->deleteLater();
         mPendingReply = nullptr;
      }
      else
      {
         emit ErrorOccurred("A request is already in progress.");
         return;
      }
   }

   const qint64 nowMs = QDateTime::currentMSecsSinceEpoch();
   if (nowMs < mNextAllowedRequestMs)
   {
      mQueuedRequest.messages = aMessages;
      mQueuedRequest.systemPrompt = aSystemPrompt;
      mQueuedRequest.tools = aTools;
      mQueuedRequest.hasValue = true;

      const int delayMs = static_cast<int>(mNextAllowedRequestMs - nowMs);
      emit DebugEvent(QStringLiteral("Rate limit delay: %1 ms").arg(delayMs));
      QTimer::singleShot(delayMs, this, [this]() {
         if (!mQueuedRequest.hasValue) {
            return;
         }
         if (mPendingReply) {
            QTimer::singleShot(100, this, [this]() {
               if (mQueuedRequest.hasValue && !mPendingReply) {
                  auto req = mQueuedRequest;
                  mQueuedRequest.hasValue = false;
                  SendChatRequest(req.messages, req.systemPrompt, req.tools);
               }
            });
            return;
         }
         auto req = mQueuedRequest;
         mQueuedRequest.hasValue = false;
         SendChatRequest(req.messages, req.systemPrompt, req.tools);
      });
      return;
   }

   // Check SSL support (builds without SSL will define QT_NO_SSL)
#ifndef QT_NO_SSL
   if (!QSslSocket::supportsSsl())
   {
      emit ErrorOccurred(QString("SSL not supported. Build version: %1, Runtime version: %2")
                            .arg(QSslSocket::sslLibraryBuildVersionString(),
                                 QSslSocket::sslLibraryVersionString()));
      return;
   }
#else
   emit ErrorOccurred("Qt was built without SSL support (QT_NO_SSL). Please install SSL-enabled Qt or use HTTP endpoint.");
   return;
#endif

   // Store tools for this request
   mCurrentTools = aTools;

   emit DebugEvent(QStringLiteral("SendChatRequest: messages=%1, tools=%2")
                     .arg(aMessages.size())
                     .arg(aTools.size()));

   // Enable SSE streaming for modern UI updates
   mStreaming       = true;
   mStreamCompleted = false;
   mStreamHasData   = false;
   mHasToolCalls    = false;
   mStreamHadError  = false;
   mStreamErrorMessage.clear();
   mStreamBuffer.clear();
   mStreamingToolCalls.clear();
   mStreamHasUsage  = false;
   mStreamPromptTokens = 0;
   mStreamCompletionTokens = 0;
   mStreamTotalTokens = 0;

   // Build body (consume mNewTrace flag)
   const bool newTrace = mNewTrace;
   mNewTrace = false;
   QByteArray body = BuildRequestBody(aMessages, aSystemPrompt, mStreaming, aTools, newTrace);
   mLastRequestBody = body;
   mRetryCount = 0;
   emit DebugEvent(QStringLiteral("Request payload bytes=%1")
                     .arg(body.size()));
   emit DebugEvent(QStringLiteral("Request payload: %1")
                     .arg(QString::fromUtf8(body)));
   SendPreparedRequest(body);
}

void Client::SendPreparedRequest(const QByteArray& aBody)
{

   // Build URL
   QString baseUrl = mPrefObjectPtr->GetBaseUrl();
   if (!baseUrl.endsWith('/'))
   {
      baseUrl += '/';
   }
   QUrl url(baseUrl + "chat/completions");
   if (!url.isValid())
   {
      emit ErrorOccurred(QString("Invalid URL: %1").arg(url.toString()));
      return;
   }

   // Build request
   QNetworkRequest request(url);
   request.setHeader(QNetworkRequest::ContentTypeHeader, "application/json");
   
   // Add API key if available
   const QString apiKey = mPrefObjectPtr->GetApiKey();
   if (!apiKey.isEmpty()) {
      request.setRawHeader("Authorization", QString("Bearer %1").arg(apiKey).toUtf8());
   }

   if (mStreaming)
   {
      request.setRawHeader("Accept", "text/event-stream");
   }

   emit RequestStarted();
   emit DebugEvent(QStringLiteral("HTTP POST %1").arg(url.toString()));

   const qint64 nowMs = QDateTime::currentMSecsSinceEpoch();
   const qint64 minIntervalMs = 300;
   if (mNextAllowedRequestMs < nowMs + minIntervalMs) {
      mNextAllowedRequestMs = nowMs + minIntervalMs;
   }

   mPendingReply = mNetworkManager->post(request, aBody);

   if (mStreaming)
   {
      connect(mPendingReply, &QNetworkReply::readyRead, this, &Client::OnReplyReadyRead);
   }

   // Set timeout — capture the specific reply pointer so that when the timer
   // fires it does NOT abort a *different* reply that started in the meantime
   // (e.g. the next agentic-loop iteration after tool calls completed).
   QNetworkReply* replyForTimeout = mPendingReply;
   QTimer::singleShot(mPrefObjectPtr->GetTimeoutMs(), this, [this, replyForTimeout]() {
      if (mPendingReply && mPendingReply == replyForTimeout)
      {
         mPendingReply->abort();
      }
   });

   connect(mPendingReply, &QNetworkReply::finished, this, &Client::OnReplyFinished);
}

void Client::CancelRequest()
{
   if (mPendingReply)
   {
      disconnect(mPendingReply, nullptr, this, nullptr);
      mPendingReply->abort();
      mPendingReply->deleteLater();
      mPendingReply = nullptr;
   }
   mStreaming       = false;
   mStreamCompleted = false;
   mStreamHasData   = false;
   mHasToolCalls    = false;
   mStreamHadError  = false;
   mStreamErrorMessage.clear();
   mStreamBuffer.clear();
   mStreamingToolCalls.clear();
   mStreamHasUsage  = false;
   mStreamPromptTokens = 0;
   mStreamCompletionTokens = 0;
   mStreamTotalTokens = 0;
   mBackoffMs = 0;
   mQueuedRequest.hasValue = false;
}

void Client::FetchModels()
{
   if (!mPrefObjectPtr || mPrefObjectPtr->GetBaseUrl().isEmpty()) {
      emit ErrorOccurred("Base URL not configured.");
      return;
   }

   // Build URL for models endpoint
   QString baseUrl = mPrefObjectPtr->GetBaseUrl();
   if (!baseUrl.endsWith('/')) {
      baseUrl += '/';
   }
   QUrl url(baseUrl + "models");
   if (!url.isValid()) {
      emit ErrorOccurred(QString("Invalid URL: %1").arg(url.toString()));
      return;
   }

   QNetworkRequest request(url);
   request.setHeader(QNetworkRequest::ContentTypeHeader, "application/json");
   
   // Add API key if available
   const QString apiKey = mPrefObjectPtr->GetApiKey();
   if (!apiKey.isEmpty()) {
      request.setRawHeader("Authorization", QString("Bearer %1").arg(apiKey).toUtf8());
   }

   emit DebugEvent(QStringLiteral("Fetching models from %1").arg(url.toString()));

   QNetworkReply* reply = mNetworkManager->get(request);
   
   connect(reply, &QNetworkReply::finished, this, [this, reply]() {
      reply->deleteLater();
      
      const int httpStatus = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
      emit DebugEvent(QStringLiteral("FetchModels HTTP status: %1").arg(httpStatus));

      if (reply->error() != QNetworkReply::NoError) {
         emit DebugEvent(QStringLiteral("FetchModels network error: %1 (%2)")
                           .arg(reply->errorString())
                           .arg(static_cast<int>(reply->error())));
         // Read the body anyway — some APIs return error details in JSON
         const QByteArray errBody = reply->readAll();
         if (!errBody.isEmpty()) {
            emit DebugEvent(QStringLiteral("FetchModels error body: %1")
                              .arg(QString::fromUtf8(errBody.left(800))));
         }
         return;
      }

      const QByteArray data = reply->readAll();
      emit DebugEvent(QStringLiteral("FetchModels response (%1 bytes): %2")
                        .arg(data.size())
                        .arg(QString::fromUtf8(data.left(800))));

      QJsonParseError parseError;
      QJsonDocument doc = QJsonDocument::fromJson(data, &parseError);
      if (parseError.error != QJsonParseError::NoError) {
         emit DebugEvent(QStringLiteral("FetchModels JSON parse error at offset %1: %2")
                           .arg(parseError.offset)
                           .arg(parseError.errorString()));
         return;
      }

      QStringList models;
      const QJsonObject root = doc.object();

      // Debug: log all top-level keys so we can see the response structure
      emit DebugEvent(QStringLiteral("FetchModels JSON keys: [%1]")
                        .arg(root.keys().join(QStringLiteral(", "))));

      const QJsonArray dataArray = root.value("data").toArray();
      emit DebugEvent(QStringLiteral("FetchModels 'data' array size: %1").arg(dataArray.size()));
      
      for (const QJsonValue& val : dataArray) {
         const QJsonObject modelObj = val.toObject();
         const QString modelId = modelObj.value("id").toString();
         if (!modelId.isEmpty()) {
            models.append(modelId);
         }
      }

      if (!models.isEmpty()) {
         models.sort();
         emit ModelsReceived(models);
         emit DebugEvent(QStringLiteral("Fetched %1 models: %2")
                           .arg(models.size())
                           .arg(models.join(QStringLiteral(", "))));
      } else {
         emit DebugEvent(QStringLiteral("FetchModels: no models found in response. "
                                        "API may not support /models endpoint or uses a different format."));
      }
   });
}

void Client::OnReplyReadyRead()
{
   if (!mPendingReply || !mStreaming)
   {
      return;
   }

   mStreamBuffer += mPendingReply->readAll();

   int newlineIndex = -1;
   while ((newlineIndex = mStreamBuffer.indexOf('\n')) != -1)
   {
      QByteArray line = mStreamBuffer.left(newlineIndex);
      mStreamBuffer.remove(0, newlineIndex + 1);

      line = line.trimmed();
      if (line.isEmpty())
      {
         continue;
      }

      if (line.startsWith("data:"))
      {
         QByteArray payload = line.mid(5).trimmed();
         emit DebugEvent(QStringLiteral("SSE data: %1")
                           .arg(QString::fromUtf8(payload)));
         if (payload == "[DONE]")
         {
            mStreamCompleted = true;
            emit DebugEvent(QStringLiteral("SSE stream done"));
            // If the stream only contained tool calls, finalize here to avoid
            // keeping a stale pending reply that blocks the next request.
            if (mHasToolCalls && !mStreamingToolCalls.isEmpty()) {
               // IMPORTANT: Clean up the current reply BEFORE emitting tool calls.
               // EmitStreamingToolCalls triggers ToolCallsReceived, which runs
               // synchronously through Task -> ExecuteToolCalls -> CheckToolCallsComplete
               // -> SendToLLM -> SendChatRequest. If mPendingReply is still set at that
               // point, it blocks the new request. Also, if SendChatRequest creates a
               // new mPendingReply, the cleanup below would destroy the NEW reply.
               if (mPendingReply) {
                  disconnect(mPendingReply, nullptr, this, nullptr);
                  mPendingReply->close();
                  mPendingReply->deleteLater();
                  mPendingReply = nullptr;
               }
               emit RequestFinished();
               // Emit usage before tool calls (so ContextManager can update before next loop)
               if (mStreamHasUsage)
               {
                  emit UsageReceived(mStreamPromptTokens, mStreamCompletionTokens, mStreamTotalTokens);
               }
               else
               {
                  // Local estimation fallback for tool-call-only streaming responses
                  const int requestBytes = mLastRequestBody.size();
                  const int estimatedPrompt = qMax(1, requestBytes * 10 / 32);
                  int completionChars = 0;
                  for (const auto& stc : mStreamingToolCalls) {
                     completionChars += stc.argumentsBuffer.size();
                  }
                  const int estimatedCompletion = qMax(1, completionChars * 10 / 32);
                  const int estimatedTotal = estimatedPrompt + estimatedCompletion;
                  emit DebugEvent(QStringLiteral("Local token estimate (tool path): prompt~%1 completion~%2 total~%3")
                                    .arg(estimatedPrompt).arg(estimatedCompletion).arg(estimatedTotal));
                  emit UsageReceived(estimatedPrompt, estimatedCompletion, estimatedTotal);
               }
               EmitStreamingToolCalls();
            }
            // NOTE: Don't emit ResponseCompleted here — we do it in OnReplyFinished
            // for normal streaming text responses.
            continue;
         }

         QJsonDocument doc = QJsonDocument::fromJson(payload);
         if (!doc.isObject())
         {
            continue;
         }

         QJsonObject root = doc.object();
         if (root.contains("error"))
         {
            QJsonObject errObj = root.value("error").toObject();
            const QString errMsg = errObj.value("message").toString();
            const QString errType = errObj.value("type").toString();
            const QString errCode = errObj.value("code").toString();
            mStreamHadError = true;
            mStreamErrorMessage = errMsg.isEmpty()
               ? QStringLiteral("Streaming error (%1)").arg(errType)
               : errMsg;
            if (errType == QStringLiteral("limit_burst_rate") ||
                errCode == QStringLiteral("limit_burst_rate"))
            {
               const int jitter = QRandomGenerator::global()->bounded(150, 401);
               mBackoffMs = (mBackoffMs <= 0) ? 600 : qMin(mBackoffMs * 2, 8000);
               mNextAllowedRequestMs = QDateTime::currentMSecsSinceEpoch() + mBackoffMs + jitter;
               emit DebugEvent(QStringLiteral("Rate limit backoff=%1 ms").arg(mBackoffMs));
            }
            emit DebugEvent(QStringLiteral("SSE error: %1").arg(mStreamErrorMessage));
            if (mPendingReply)
            {
               mPendingReply->abort();
            }
            continue;
         }

         // Extract token usage if present (OpenAI sends usage in the final SSE chunk)
         if (root.contains("usage"))
         {
            const QJsonObject usageObj = root["usage"].toObject();
            int prompt = 0;
            int completion = 0;
            int total = 0;
            if (ExtractUsage(usageObj, prompt, completion, total))
            {
               mStreamPromptTokens = prompt;
               mStreamCompletionTokens = completion;
               mStreamTotalTokens = total;
               mStreamHasUsage = true;
               emit DebugEvent(QStringLiteral("SSE usage: prompt=%1 completion=%2 total=%3")
                                 .arg(mStreamPromptTokens)
                                 .arg(mStreamCompletionTokens)
                                 .arg(mStreamTotalTokens));
            }
         }

         if (!root.contains("choices"))
         {
            continue;
         }

         QJsonArray choices = root["choices"].toArray();
         if (choices.isEmpty())
         {
            continue;
         }

         QJsonObject choice = choices[0].toObject();
         if (choice.contains("delta"))
         {
            QJsonObject delta = choice["delta"].toObject();

            // Handle text content chunks
            if (delta.contains("content"))
            {
               QString chunk = delta["content"].toString();
               if (!chunk.isEmpty())
               {
                  mStreamHasData = true;
                  emit DebugEvent(QStringLiteral("SSE content chunk: %1").arg(chunk));
                  emit ResponseChunkReceived(chunk);
               }
            }

            // Handle streaming tool_calls
            if (delta.contains("tool_calls"))
            {
               mHasToolCalls = true;
               emit DebugEvent(QStringLiteral("SSE tool_calls payload: %1")
                                 .arg(QString::fromUtf8(payload)));
               emit DebugEvent(QStringLiteral("SSE tool_calls chunk"));
               QJsonArray toolCallsArr = delta["tool_calls"].toArray();
               for (const auto& tcVal : toolCallsArr)
               {
                  QJsonObject tc    = tcVal.toObject();
                  int         index = tc["index"].toInt();

                  // Grow our list if needed
                  while (mStreamingToolCalls.size() <= index)
                  {
                     mStreamingToolCalls.append(StreamingToolCall());
                  }

                  // Compatibility: some LLM backends (e.g. Ollama) send multiple
                  // tool calls all with index=0 instead of incrementing.  Detect
                  // this by checking whether the slot already has a different id.
                  const QString incomingId = tc.value("id").toString();
                  if (!incomingId.isEmpty()
                      && !mStreamingToolCalls[index].id.isEmpty()
                      && mStreamingToolCalls[index].id != incomingId)
                  {
                     // This is a NEW tool call that the backend mis-indexed.
                     // Push it to the end of the list instead of overwriting.
                     mStreamingToolCalls.append(StreamingToolCall());
                     index = mStreamingToolCalls.size() - 1;
                     emit DebugEvent(QStringLiteral("SSE duplicate index detected, "
                                                    "remapped to index=%1").arg(index));
                  }

                  auto& stc = mStreamingToolCalls[index];

                  if (tc.contains("id"))
                  {
                     const QString id = tc["id"].toString();
                     if (!id.isEmpty())
                     {
                        stc.id = id;
                     }
                  }
                  if (tc.contains("function"))
                  {
                     QJsonObject fn = tc["function"].toObject();
                     if (fn.contains("name"))
                     {
                        const QString name = fn["name"].toString();
                        if (!name.isEmpty())
                        {
                           stc.functionName = name;
                           // Notify UI that a tool call is being streamed
                           emit ToolCallStreaming(name, stc.argumentsBuffer.toUtf8().size());
                        }
                        emit DebugEvent(QStringLiteral("SSE tool_call name=%1 index=%2")
                                          .arg(stc.functionName)
                                          .arg(index));
                     }
                     if (fn.contains("arguments"))
                     {
                        const QJsonValue argsVal = fn["arguments"];
                        if (argsVal.isString()) {
                           const QString argChunk = argsVal.toString();
                           stc.argumentsBuffer += argChunk;
                           // Notify UI of streaming progress (every chunk)
                           if (!stc.functionName.isEmpty()) {
                              emit ToolCallStreaming(stc.functionName, stc.argumentsBuffer.toUtf8().size());
                           }
                           emit DebugEvent(QStringLiteral("SSE tool_call args chunk index=%1: %2")
                                             .arg(index)
                                             .arg(argChunk));
                        } else if (argsVal.isObject()) {
                           const QJsonDocument argDoc(argsVal.toObject());
                           stc.argumentsBuffer = QString::fromUtf8(argDoc.toJson(QJsonDocument::Compact));
                           emit DebugEvent(QStringLiteral("SSE tool_call args object index=%1")
                                             .arg(index));
                        } else if (argsVal.isArray()) {
                           const QJsonDocument argDoc(argsVal.toArray());
                           stc.argumentsBuffer = QString::fromUtf8(argDoc.toJson(QJsonDocument::Compact));
                           emit DebugEvent(QStringLiteral("SSE tool_call args array index=%1")
                                             .arg(index));
                        }
                     }
                  }
               }
            }
         }
      }
   }
}

void Client::OnReplyFinished()
{
   // Use sender() to identify which reply triggered this slot.
   // After the [DONE] handler clears mPendingReply and starts a new request,
   // the old reply's finished signal may still fire (via deleteLater).
   // Without this check, we'd accidentally process/nullify the new reply.
   QNetworkReply* reply = qobject_cast<QNetworkReply*>(sender());
   if (!reply || reply != mPendingReply)
   {
      return;
   }

   mPendingReply = nullptr;

   emit RequestFinished();

   if (reply->error() == QNetworkReply::OperationCanceledError)
   {
      emit DebugEvent(QStringLiteral("HTTP request cancelled"));
      if (mStreamHadError)
      {
         emit ErrorOccurred(mStreamErrorMessage);
         mStreamHadError = false;
         mStreamErrorMessage.clear();
      }
      else
      {
         emit ErrorOccurred("Request timed out or was cancelled.");
      }
      reply->deleteLater();
      return;
   }

   if (mStreamHadError)
   {
      emit ErrorOccurred(mStreamErrorMessage);
      mStreamHadError = false;
      mStreamErrorMessage.clear();
      mBackoffMs = 0;
      reply->deleteLater();
      return;
   }

   if (reply->error() != QNetworkReply::NoError)
   {
      QString errorMsg = QString("Network error: %1").arg(reply->errorString());
      emit DebugEvent(QStringLiteral("HTTP error: %1").arg(errorMsg));

      const int httpStatus = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
      const bool isTooManyRequests = (httpStatus == 429) ||
         reply->errorString().contains(QStringLiteral("Too Many Requests"), Qt::CaseInsensitive);
      
      // Try to parse error response body
      QByteArray responseData = reply->readAll();
      if (!responseData.isEmpty())
      {
         QJsonDocument doc = QJsonDocument::fromJson(responseData);
         if (doc.isObject())
         {
            QJsonObject obj = doc.object();
            if (obj.contains("error"))
            {
               QJsonObject errorObj = obj["error"].toObject();
               if (errorObj.contains("message"))
               {
                  errorMsg = errorObj["message"].toString();
               }
            }
         }
      }
      
      if (isTooManyRequests && mRetryCount < mMaxRetries && !mLastRequestBody.isEmpty())
      {
         ++mRetryCount;
         int retryAfterMs = 0;
         const QByteArray retryAfterHeader = reply->rawHeader("Retry-After");
         if (!retryAfterHeader.isEmpty())
         {
            bool okSeconds = false;
            const int seconds = QString::fromUtf8(retryAfterHeader).trimmed().toInt(&okSeconds);
            if (okSeconds && seconds > 0) {
               retryAfterMs = seconds * 1000;
            } else {
               const QDateTime retryAt = QDateTime::fromString(
                  QString::fromUtf8(retryAfterHeader).trimmed(), Qt::RFC2822Date);
               if (retryAt.isValid()) {
                  const qint64 diffMs = QDateTime::currentDateTimeUtc().msecsTo(retryAt.toUTC());
                  if (diffMs > 0) {
                     retryAfterMs = static_cast<int>(diffMs);
                  }
               }
            }
         }

         const int jitter = QRandomGenerator::global()->bounded(150, 451);
         mBackoffMs = (mBackoffMs <= 0) ? 800 : qMin(mBackoffMs * 2, 60000);
         const int delayMs = qMax(mBackoffMs, retryAfterMs) + jitter;
         mNextAllowedRequestMs = QDateTime::currentMSecsSinceEpoch() + delayMs;

         emit DebugEvent(QStringLiteral("HTTP 429 retry %1/%2 scheduled in %3 ms")
                           .arg(mRetryCount)
                           .arg(mMaxRetries)
                           .arg(delayMs));

         // Reset stream state before retry
         mStreamCompleted = false;
         mStreamHasData = false;
         mHasToolCalls = false;
         mStreamHadError = false;
         mStreamErrorMessage.clear();
         mStreamBuffer.clear();
         mStreamingToolCalls.clear();
         mStreamHasUsage = false;
         mStreamPromptTokens = 0;
         mStreamCompletionTokens = 0;
         mStreamTotalTokens = 0;

         QTimer::singleShot(delayMs, this, [this]() {
            if (mPendingReply) {
               return;
            }
            if (!mLastRequestBody.isEmpty()) {
               SendPreparedRequest(mLastRequestBody);
            }
         });

         reply->deleteLater();
         return;
      }

      emit ErrorOccurred(errorMsg);
      mBackoffMs = 0;
      reply->deleteLater();
      return;
   }

   mBackoffMs = 0;

   QByteArray responseData = reply->readAll();
   emit DebugEvent(QStringLiteral("HTTP response bytes=%1").arg(responseData.size()));
   if (!responseData.isEmpty())
   {
      emit DebugEvent(QStringLiteral("HTTP response body: %1")
                        .arg(QString::fromUtf8(responseData)));
   }

   // Capture completion char count BEFORE tool call buffers are cleared
   int completionCharsForEstimate = mStreamBuffer.size();
   for (const auto& stc : mStreamingToolCalls) {
      completionCharsForEstimate += stc.argumentsBuffer.size();
   }

   if (mStreaming)
   {
      // If we accumulated tool calls during streaming, emit them now
      if (mHasToolCalls && !mStreamingToolCalls.isEmpty())
      {
         EmitStreamingToolCalls();
      }
      // If we never received streaming data, fall back to non-stream parsing.
      else if (!mStreamHasData && !mHasToolCalls)
      {
         QList<ToolCall> toolCalls;
         QString response = ParseResponse(responseData, &toolCalls);
         if (!toolCalls.isEmpty())
         {
            emit DebugEvent(QStringLiteral("Non-stream tool_calls: count=%1")
                              .arg(toolCalls.size()));
            emit ToolCallsReceived(toolCalls);
         }
         else if (response.isEmpty())
         {
            emit ErrorOccurred("Empty or invalid response from API.");
         }
         else
         {
            emit DebugEvent(QStringLiteral("Non-stream response len=%1")
                              .arg(response.size()));
            emit ResponseReceived(response);
         }
      }
      else
      {
         // Normal streaming text completion — emit ResponseCompleted
         emit DebugEvent(QStringLiteral("Streaming response completed"));
         emit ResponseCompleted();
      }
   }
   else
   {
      QList<ToolCall> toolCalls;
      QString response = ParseResponse(responseData, &toolCalls);
      if (!toolCalls.isEmpty())
      {
         emit DebugEvent(QStringLiteral("Non-stream tool_calls: count=%1")
                           .arg(toolCalls.size()));
         emit ToolCallsReceived(toolCalls);
      }
      else if (response.isEmpty())
      {
         emit ErrorOccurred("Empty or invalid response from API.");
      }
      else
      {
         emit DebugEvent(QStringLiteral("Non-stream response len=%1")
                           .arg(response.size()));
         emit ResponseReceived(response);
      }
   }

   // Emit token usage for context management.
   // For streaming: extracted from the SSE usage chunk.
   // For non-streaming: extract from the full response body.
   bool usageEmitted = false;
   if (mStreamHasUsage)
   {
      emit UsageReceived(mStreamPromptTokens, mStreamCompletionTokens, mStreamTotalTokens);
      usageEmitted = true;
   }
   else if (!responseData.isEmpty())
   {
      // Try to parse usage from the response body (non-streaming or fallback)
      const QJsonDocument usageDoc = QJsonDocument::fromJson(responseData);
      if (usageDoc.isObject())
      {
         const QJsonObject usageRoot = usageDoc.object();
         if (usageRoot.contains("usage"))
         {
            const QJsonObject usageObj = usageRoot["usage"].toObject();
            int prompt = 0;
            int completion = 0;
            int total = 0;
            if (ExtractUsage(usageObj, prompt, completion, total))
            {
               emit UsageReceived(prompt, completion, total);
               usageEmitted = true;
            }
         }
      }
   }

   // Local estimation fallback: if the API did not return usage info,
   // estimate tokens from the request payload size.
   // Rough heuristic: ~3.2 chars per token as a blended average.
   if (!usageEmitted)
   {
      const int requestBytes = mLastRequestBody.size();
      const int estimatedPrompt = qMax(1, requestBytes * 10 / 32);
      const int estimatedCompletion = qMax(1, completionCharsForEstimate * 10 / 32);
      const int estimatedTotal = estimatedPrompt + estimatedCompletion;
      emit DebugEvent(QStringLiteral("Local token estimate: prompt~%1 completion~%2 total~%3")
                        .arg(estimatedPrompt)
                        .arg(estimatedCompletion)
                        .arg(estimatedTotal));
      emit UsageReceived(estimatedPrompt, estimatedCompletion, estimatedTotal);
   }

   reply->deleteLater();
}

void Client::EmitStreamingToolCalls()
{
   if (!mHasToolCalls || mStreamingToolCalls.isEmpty()) {
      return;
   }

   QList<ToolCall> toolCalls;
   for (const auto& stc : mStreamingToolCalls)
   {
      if (stc.functionName.isEmpty()) {
         continue;
      }
      ToolCall tc;
      tc.id           = stc.id;
      tc.functionName = stc.functionName;
      QJsonDocument argDoc = QJsonDocument::fromJson(stc.argumentsBuffer.toUtf8());
      tc.arguments = argDoc.isObject() ? argDoc.object() : QJsonObject();
      toolCalls.append(tc);
   }

   if (!toolCalls.isEmpty()) {
      emit DebugEvent(QStringLiteral("Streaming tool_calls complete: count=%1")
                        .arg(toolCalls.size()));
      emit ToolCallsReceived(toolCalls);
   }

   mStreamingToolCalls.clear();
   mHasToolCalls = false;
}

QByteArray Client::BuildRequestBody(const QList<ChatMessage>& aMessages,
                                    const QString& aSystemPrompt,
                                    bool aStream,
                                    const QJsonArray& aTools,
                                    bool aNewTrace) const
{
   QJsonObject root;
   root["model"] = mPrefObjectPtr->GetModel();
   if (aStream)
   {
      root["stream"] = true;
      // Request usage statistics in the final SSE chunk (OpenAI-compatible)
      QJsonObject streamOptions;
      streamOptions["include_usage"] = true;
      root["stream_options"] = streamOptions;
   }

   // Include tools if provided.
   // When tools are empty (e.g. auto-condense summary request), omit tool_choice entirely.
   // Some API backends (e.g. Anthropic) reject requests where tool_choice is present
   // but tools is absent: "When using tool_choice, tools must be set".
   if (!aTools.isEmpty())
   {
      root["tools"]       = aTools;
      root["tool_choice"] = QString("auto");
   }
   // No else: do not send tool_choice when tools are absent.

   QJsonArray messages;

   // Add system prompt if provided
   if (!aSystemPrompt.isEmpty())
   {
      QJsonObject sysMsg;
      sysMsg["role"]    = "system";
      sysMsg["content"] = aSystemPrompt;
      messages.append(sysMsg);
   }

   // Add conversation messages
   for (const auto& msg : aMessages)
   {
      QJsonObject jsonMsg;
      switch (msg.mRole)
      {
         case MessageRole::System:
            jsonMsg["role"] = "system";
            break;
         case MessageRole::User:
            jsonMsg["role"] = "user";
            break;
         case MessageRole::Assistant:
            jsonMsg["role"] = "assistant";
            // If assistant message had tool_calls, serialize them
            if (!msg.mToolCalls.isEmpty())
            {
               QJsonArray tcArr;
               for (const auto& tc : msg.mToolCalls)
               {
                  QJsonObject tcObj;
                  tcObj["id"]   = tc.id;
                  tcObj["type"] = QString("function");
                  QJsonObject fnObj;
                  fnObj["name"]      = tc.functionName;
                  fnObj["arguments"] = QString::fromUtf8(
                     QJsonDocument(tc.arguments).toJson(QJsonDocument::Compact));
                  tcObj["function"] = fnObj;
                  tcArr.append(tcObj);
               }
               jsonMsg["tool_calls"] = tcArr;
               // When tool_calls are present, content may be null
               if (msg.mContent.isEmpty())
               {
                  jsonMsg["content"] = QJsonValue(QJsonValue::Null);
               }
            }
            break;
         case MessageRole::Tool:
            jsonMsg["role"]         = "tool";
            jsonMsg["tool_call_id"] = msg.mToolCallId;
            break;
      }
      if (!jsonMsg.contains("content") || jsonMsg["content"].type() == QJsonValue::Undefined)
      {
         jsonMsg["content"] = msg.mContent;
      }
      messages.append(jsonMsg);
   }

   root["messages"] = messages;

   // Metadata for Langfuse trace grouping (proxy extracts session_id / new_trace).
   // Only attach when routing through a local proxy; direct upstream APIs (e.g. MiniMax)
   // reject unknown top-level fields and return HTTP 400.
   auto isLocalUrl = [](const QString& url) -> bool {
      const QString lower = url.toLower();
      return lower.contains("://localhost") ||
             lower.contains("://127.")      ||
             lower.contains("://[::1]");
   };
   if (!mSessionId.isEmpty() && isLocalUrl(mPrefObjectPtr->GetBaseUrl()))
   {
      QJsonObject metadata;
      metadata["session_id"] = mSessionId;
      if (aNewTrace)
      {
         metadata["new_trace"] = true;
      }
      root["metadata"] = metadata;
   }

   return QJsonDocument(root).toJson(QJsonDocument::Compact);
}

QString Client::ParseResponse(const QByteArray& aResponseData, QList<ToolCall>* aToolCalls) const
{
   QJsonDocument doc = QJsonDocument::fromJson(aResponseData);
   if (!doc.isObject())
   {
      return QString();
   }

   QJsonObject root = doc.object();
   
   // OpenAI-compatible format
   if (root.contains("choices"))
   {
      QJsonArray choices = root["choices"].toArray();
      if (!choices.isEmpty())
      {
         QJsonObject choice  = choices[0].toObject();
         QJsonObject message = choice["message"].toObject();

         // Check for tool_calls in response
         if (aToolCalls && message.contains("tool_calls"))
         {
            QJsonArray tcArr = message["tool_calls"].toArray();
            for (const auto& tcVal : tcArr)
            {
               QJsonObject tcObj = tcVal.toObject();
               ToolCall tc;
               tc.id = tcObj["id"].toString();
               QJsonObject fnObj = tcObj["function"].toObject();
               tc.functionName = fnObj["name"].toString();
               const QJsonValue argsVal = fnObj["arguments"];
               if (argsVal.isObject()) {
                  tc.arguments = argsVal.toObject();
               } else if (argsVal.isString()) {
                  QJsonDocument argDoc = QJsonDocument::fromJson(
                     argsVal.toString().toUtf8());
                  tc.arguments = argDoc.isObject() ? argDoc.object() : QJsonObject();
               } else if (argsVal.isArray()) {
                  QJsonObject wrapper;
                  wrapper["items"] = argsVal.toArray();
                  tc.arguments = wrapper;
               } else {
                  tc.arguments = QJsonObject();
               }
               aToolCalls->append(tc);
            }
         }

         return message["content"].toString();
      }
   }

   return QString();
}

} // namespace AiChat
