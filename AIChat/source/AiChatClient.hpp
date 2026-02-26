// -----------------------------------------------------------------------------
// File: AiChatClient.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_CLIENT_HPP
#define AICHAT_CLIENT_HPP

#include <QByteArray>
#include <QJsonArray>
#include <QList>
#include <QObject>
#include <QString>
#include <QStringList>

#include "tools/AiChatToolTypes.hpp"

class QNetworkAccessManager;
class QNetworkReply;

namespace AiChat
{
class PrefObject;

//! Message role enumeration
enum class MessageRole
{
   System,
   User,
   Assistant,
   Tool       ///< Tool execution result fed back to the LLM
};

//! A single message in the conversation
struct ChatMessage
{
   MessageRole     mRole;
   QString         mContent;

   //! Tool calls returned by the assistant (only set when mRole == Assistant)
   QList<ToolCall> mToolCalls;

   //! For Tool role messages: the tool_call_id this result corresponds to
   QString         mToolCallId;

   //! For Tool role messages: the tool name (e.g. "read_file") — simplifies dedup lookup
   QString         mToolName;
};

//! HTTP client for AI chat API (OpenAI-compatible format)
class Client : public QObject
{
   Q_OBJECT
public:
   //! Constructs a Client
   //! @param aPrefObject is the preference object containing API configuration
   //! @param aParent is the parent QObject
   explicit Client(PrefObject* aPrefObject, QObject* aParent = nullptr);
   //! Destructs a Client
   ~Client() override;

   //! Send a chat completion request (simple, no tools)
   //! @param aMessages is the list of messages in the conversation
   //! @param aSystemPrompt is an optional system prompt to prepend
   void SendChatRequest(const QList<ChatMessage>& aMessages, const QString& aSystemPrompt = QString());

   //! Send a chat completion request with tool definitions (function-calling)
   //! @param aMessages is the list of messages in the conversation
   //! @param aSystemPrompt is the system prompt to prepend
   //! @param aTools is the JSON array of tool definitions (OpenAI format)
   void SendChatRequest(const QList<ChatMessage>& aMessages, const QString& aSystemPrompt, const QJsonArray& aTools);

   //! Fetch available models from the API
   void FetchModels();

   //! Cancel any pending request
   void CancelRequest();

   //! Check if a request is in progress
   bool IsRequestInProgress() const noexcept { return mPendingReply != nullptr; }

   //! Set the session identifier (for Langfuse trace grouping)
   void SetSessionId(const QString& aSessionId);

   //! Mark that the next request starts a new conversation trace
   void MarkNewTrace() { mNewTrace = true; }

signals:
   //! Emitted when a response is received
   //! @param aResponse is the assistant's response text
   void ResponseReceived(const QString& aResponse);

   //! Emitted when a streaming response chunk is received
   //! @param aChunk is the next chunk of assistant text
   void ResponseChunkReceived(const QString& aChunk);

   //! Emitted when a streaming response completes
   void ResponseCompleted();

   //! Emitted when the assistant returns one or more tool calls
   void ToolCallsReceived(const QList<ToolCall>& aToolCalls);

   //! Emitted during streaming when tool_call arguments are being accumulated.
   //! Allows the UI to show progress during potentially long file-write payloads.
   //! @param aFunctionName  The tool being called (e.g. "write_to_file")
   //! @param aArgBytes      Total accumulated argument bytes so far
   void ToolCallStreaming(const QString& aFunctionName, int aArgBytes);

   //! Emitted when models list is fetched from API
   void ModelsReceived(const QStringList& aModels);

   //! Emitted when an error occurs
   //! @param aError is the error message
   void ErrorOccurred(const QString& aError);

   //! Emitted when the request starts
   void RequestStarted();

   //! Emitted when the request finishes (success or error)
   void RequestFinished();

   //! Emitted when the API returns token usage information
   void UsageReceived(int aPromptTokens, int aCompletionTokens, int aTotalTokens);

   //! Emitted for debug logging
   void DebugEvent(const QString& aMessage);

private slots:
   void OnReplyFinished();
   void OnReplyReadyRead();

private:
   //! Build the request JSON body
   QByteArray BuildRequestBody(const QList<ChatMessage>& aMessages,
                               const QString& aSystemPrompt,
                               bool aStream,
                               const QJsonArray& aTools = QJsonArray(),
                               bool aNewTrace = false) const;

   //! Parse the response JSON (returns content text; populates aToolCalls if present)
   QString ParseResponse(const QByteArray& aResponseData,
                         QList<ToolCall>* aToolCalls = nullptr) const;

   //! Internal: send the prepared request body
   void SendPreparedRequest(const QByteArray& aBody);

   //! Emit accumulated streaming tool calls and clear buffers
   void EmitStreamingToolCalls();

   struct QueuedRequest
   {
      QList<ChatMessage> messages;
      QString            systemPrompt;
      QJsonArray         tools;
      bool               hasValue{false};
   };

   PrefObject*            mPrefObjectPtr;
   QNetworkAccessManager* mNetworkManager;
   QNetworkReply*         mPendingReply;
   QByteArray             mStreamBuffer;
   bool                   mStreaming{false};
   bool                   mStreamCompleted{false};
   bool                   mStreamHasData{false};
   bool                   mStreamHadError{false};
   QString                mStreamErrorMessage;

   //! Accumulate streaming tool calls
   struct StreamingToolCall
   {
      QString id;
      QString functionName;
      QString argumentsBuffer;   ///< accumulates JSON string chunks
   };
   QList<StreamingToolCall> mStreamingToolCalls;
   bool                     mHasToolCalls{false};

   //! Stored tools array for the current request
   QJsonArray mCurrentTools;

   //! Session ID for Langfuse trace grouping
   QString mSessionId;
   //! When true, the next request will tell the proxy to start a fresh trace
   bool    mNewTrace{false};

   //! Raw request body for retry (HTTP 429 backoff)
   QByteArray mLastRequestBody;

   //! Streaming usage tracking (extracted from the final SSE chunk)
   int mStreamPromptTokens{0};
   int mStreamCompletionTokens{0};
   int mStreamTotalTokens{0};
   bool mStreamHasUsage{false};

   //! Retry state for HTTP 429
   int mRetryCount{0};
   int mMaxRetries{5};

   //! Rate-limit/backoff handling
   QueuedRequest mQueuedRequest;
   qint64        mNextAllowedRequestMs{0};
   int           mBackoffMs{0};
};

} // namespace AiChat

#endif // AICHAT_CLIENT_HPP
