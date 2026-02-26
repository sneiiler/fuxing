// -----------------------------------------------------------------------------
// File: AiChatSessionManager.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatSessionManager.hpp"

#include <QDir>
#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QStandardPaths>
#include <QUuid>

namespace AiChat
{

// ============================================================================
// Anonymous-namespace helpers
// ============================================================================

namespace
{

QJsonObject MessageToJsonImpl(const ChatMessage& aMsg)
{
   QJsonObject obj;
   switch (aMsg.mRole)
   {
      case MessageRole::System:    obj["role"] = QStringLiteral("system");    break;
      case MessageRole::User:      obj["role"] = QStringLiteral("user");      break;
      case MessageRole::Assistant: obj["role"] = QStringLiteral("assistant"); break;
      case MessageRole::Tool:      obj["role"] = QStringLiteral("tool");      break;
   }
   obj["content"] = aMsg.mContent;

   if (!aMsg.mToolCalls.isEmpty())
   {
      QJsonArray tcArr;
      for (const auto& tc : aMsg.mToolCalls)
      {
         QJsonObject tcObj;
         tcObj["id"]            = tc.id;
         tcObj["function_name"] = tc.functionName;
         tcObj["arguments"]     = tc.arguments;
         tcArr.append(tcObj);
      }
      obj["tool_calls"] = tcArr;
   }

   if (!aMsg.mToolCallId.isEmpty())
   {
      obj["tool_call_id"] = aMsg.mToolCallId;
   }

   if (!aMsg.mToolName.isEmpty())
   {
      obj["tool_name"] = aMsg.mToolName;
   }

   return obj;
}

ChatMessage MessageFromJsonImpl(const QJsonObject& aObj)
{
   ChatMessage msg;
   const QString role = aObj["role"].toString();
   if      (role == QLatin1String("system"))    msg.mRole = MessageRole::System;
   else if (role == QLatin1String("user"))      msg.mRole = MessageRole::User;
   else if (role == QLatin1String("assistant")) msg.mRole = MessageRole::Assistant;
   else if (role == QLatin1String("tool"))      msg.mRole = MessageRole::Tool;

   msg.mContent    = aObj["content"].toString();
   msg.mToolCallId = aObj["tool_call_id"].toString();
   msg.mToolName   = aObj["tool_name"].toString();  // optional, empty if absent

   const QJsonArray tcArr = aObj["tool_calls"].toArray();
   for (const auto& v : tcArr)
   {
      const QJsonObject tcObj = v.toObject();
      ToolCall tc;
      tc.id           = tcObj["id"].toString();
      tc.functionName = tcObj["function_name"].toString();
      tc.arguments    = tcObj["arguments"].toObject();
      msg.mToolCalls.append(tc);
   }

   return msg;
}

QJsonObject InfoToJson(const SessionInfo& aInfo)
{
   QJsonObject obj;
   obj["id"]            = aInfo.id;
   obj["title"]         = aInfo.title;
   obj["created_at"]    = aInfo.createdAt.toString(Qt::ISODate);
   obj["updated_at"]    = aInfo.updatedAt.toString(Qt::ISODate);
   obj["message_count"] = aInfo.messageCount;
   return obj;
}

SessionInfo InfoFromJson(const QJsonObject& aObj)
{
   SessionInfo info;
   info.id           = aObj["id"].toString();
   info.title        = aObj["title"].toString();
   info.createdAt    = QDateTime::fromString(aObj["created_at"].toString(), Qt::ISODate);
   info.updatedAt    = QDateTime::fromString(aObj["updated_at"].toString(), Qt::ISODate);
   info.messageCount = aObj["message_count"].toInt();
   return info;
}

} // anonymous namespace

// ============================================================================
// Construction
// ============================================================================

SessionManager::SessionManager(QObject* aParent)
   : QObject(aParent)
{
}

// ============================================================================
// Configuration
// ============================================================================

void SessionManager::SetStorageDirectory(const QString& aDir)
{
   mStorageDir    = aDir;
   mIndexLoaded   = false;
   mSessionIndex.clear();
}

// ============================================================================
// CRUD Operations
// ============================================================================

SessionInfo SessionManager::CreateSession(const QString& aTitle)
{
   SessionInfo info;
   info.id           = QUuid::createUuid().toString(QUuid::WithoutBraces);
   info.title        = aTitle.isEmpty() ? QStringLiteral("New Chat") : aTitle;
   info.createdAt    = QDateTime::currentDateTime();
   info.updatedAt    = info.createdAt;
   info.messageCount = 0;

   LoadIndex();
   mSessionIndex.prepend(info);
   mCurrentSessionId = info.id;
   SaveIndex();

   emit SessionListChanged();
   return info;
}

void SessionManager::SaveSession(const QString& aSessionId,
                                  const QList<ChatMessage>& aHistory)
{
   if (aSessionId.isEmpty())
   {
      return;
   }

   // Count user + assistant messages
   int count = 0;
   for (const auto& msg : aHistory)
   {
      if (msg.mRole == MessageRole::User || msg.mRole == MessageRole::Assistant)
      {
         ++count;
      }
   }

   // Don't persist empty conversations
   if (count == 0)
   {
      return;
   }

   EnsureStorageDir();
   LoadIndex();

   for (auto& info : mSessionIndex)
   {
      if (info.id == aSessionId)
      {
         // Title is managed by Service via RenameSession (LLM-generated).
         // Do NOT auto-rename here so the LLM title logic is not bypassed.
         info.updatedAt    = QDateTime::currentDateTime();
         info.messageCount = count;

         // Serialise messages
         QJsonArray messagesArr;
         for (const auto& msg : aHistory)
         {
            messagesArr.append(MessageToJsonImpl(msg));
         }

         QJsonObject root;
         root["info"]     = InfoToJson(info);
         root["messages"] = messagesArr;

         QFile file(SessionFilePath(aSessionId));
         if (file.open(QIODevice::WriteOnly | QIODevice::Truncate))
         {
            file.write(QJsonDocument(root).toJson(QJsonDocument::Compact));
            file.close();
         }

         SaveIndex();
         emit SessionListChanged();
         return;
      }
   }
}

SessionData SessionManager::LoadSession(const QString& aSessionId)
{
   SessionData data;

   QFile file(SessionFilePath(aSessionId));
   if (!file.open(QIODevice::ReadOnly))
   {
      return data;
   }

   const QJsonDocument doc = QJsonDocument::fromJson(file.readAll());
   file.close();

   if (!doc.isObject())
   {
      return data;
   }

   const QJsonObject root = doc.object();
   data.info = InfoFromJson(root["info"].toObject());

   const QJsonArray messagesArr = root["messages"].toArray();
   for (const auto& v : messagesArr)
   {
      data.history.append(MessageFromJsonImpl(v.toObject()));
   }

   return data;
}

QList<SessionInfo> SessionManager::ListSessions()
{
   LoadIndex();
   return mSessionIndex;
}

void SessionManager::DeleteSession(const QString& aSessionId)
{
   LoadIndex();

   for (int i = 0; i < mSessionIndex.size(); ++i)
   {
      if (mSessionIndex[i].id == aSessionId)
      {
         mSessionIndex.removeAt(i);
         break;
      }
   }

   QFile::remove(SessionFilePath(aSessionId));
   SaveIndex();
   emit SessionListChanged();
}

void SessionManager::RenameSession(const QString& aSessionId, const QString& aNewTitle)
{
   LoadIndex();

   for (auto& info : mSessionIndex)
   {
      if (info.id == aSessionId)
      {
         info.title = aNewTitle;
         break;
      }
   }

   SaveIndex();
   emit SessionListChanged();
}

QString SessionManager::SessionTitle(const QString& aSessionId)
{
   LoadIndex();

   for (const auto& info : mSessionIndex)
   {
      if (info.id == aSessionId)
      {
         return info.title;
      }
   }
   return QStringLiteral("New Chat");
}

// ============================================================================
// Storage Helpers
// ============================================================================

QString SessionManager::SessionDir() const
{
   if (!mStorageDir.isEmpty())
   {
      return mStorageDir;
   }
   return QStandardPaths::writableLocation(QStandardPaths::AppDataLocation)
          + QStringLiteral("/aichat_sessions");
}

QString SessionManager::SessionFilePath(const QString& aSessionId) const
{
   return SessionDir() + QStringLiteral("/") + aSessionId + QStringLiteral(".json");
}

QString SessionManager::IndexFilePath() const
{
   return SessionDir() + QStringLiteral("/sessions_index.json");
}

void SessionManager::EnsureStorageDir()
{
   QDir dir(SessionDir());
   if (!dir.exists())
   {
      dir.mkpath(QStringLiteral("."));
   }
}

void SessionManager::LoadIndex()
{
   if (mIndexLoaded)
   {
      return;
   }

   EnsureStorageDir();

   QFile file(IndexFilePath());
   if (file.open(QIODevice::ReadOnly))
   {
      const QJsonDocument doc = QJsonDocument::fromJson(file.readAll());
      file.close();

      if (doc.isArray())
      {
         const QJsonArray arr = doc.array();
         for (const auto& v : arr)
         {
            mSessionIndex.append(InfoFromJson(v.toObject()));
         }
      }
   }

   mIndexLoaded = true;
}

void SessionManager::SaveIndex()
{
   EnsureStorageDir();

   QJsonArray arr;
   for (const auto& info : mSessionIndex)
   {
      arr.append(InfoToJson(info));
   }

   QFile file(IndexFilePath());
   if (file.open(QIODevice::WriteOnly | QIODevice::Truncate))
   {
      file.write(QJsonDocument(arr).toJson(QJsonDocument::Compact));
      file.close();
   }
}

QString SessionManager::GenerateTitle(const QList<ChatMessage>& aHistory)
{
   QString combinedText;
   int userMsgCount = 0;

   for (const auto& msg : aHistory)
   {
      if (msg.mRole == MessageRole::User && !msg.mContent.trimmed().isEmpty())
      {
         if (!combinedText.isEmpty())
         {
            combinedText += QStringLiteral(" ");
         }
         combinedText += msg.mContent.trimmed();
         ++userMsgCount;

         if (userMsgCount >= 2)
         {
            break;
         }
      }
   }

   if (!combinedText.isEmpty())
   {
      combinedText.replace('\n', ' ');
      combinedText.replace('\r', ' ');
      // Collapse consecutive spaces
      combinedText = combinedText.simplified();
      
      if (combinedText.size() > 30)
      {
         combinedText = combinedText.left(27) + QStringLiteral("...");
      }
      return combinedText;
   }

   return QStringLiteral("New Chat");
}

// ---- Static JSON helpers (forwarded to namespace-scope lambdas) ----

QJsonObject SessionManager::MessageToJson(const ChatMessage& aMsg)
{
   return MessageToJsonImpl(aMsg);
}

ChatMessage SessionManager::MessageFromJson(const QJsonObject& aObj)
{
   return MessageFromJsonImpl(aObj);
}

QJsonObject SessionManager::SessionInfoToJson(const SessionInfo& aInfo)
{
   return InfoToJson(aInfo);
}

SessionInfo SessionManager::SessionInfoFromJson(const QJsonObject& aObj)
{
   return InfoFromJson(aObj);
}

} // namespace AiChat
