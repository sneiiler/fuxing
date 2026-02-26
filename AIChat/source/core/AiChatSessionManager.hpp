// -----------------------------------------------------------------------------
// File: AiChatSessionManager.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_SESSION_MANAGER_HPP
#define AICHAT_SESSION_MANAGER_HPP

#include <QDateTime>
#include <QList>
#include <QObject>
#include <QString>

#include "AiChatClient.hpp"

namespace AiChat
{

/// Lightweight metadata for a session (suitable for list display).
struct SessionInfo
{
   QString   id;                ///< Unique identifier (UUID without braces)
   QString   title;             ///< Display title
   QDateTime createdAt;         ///< Creation timestamp
   QDateTime updatedAt;         ///< Last-update timestamp
   int       messageCount{0};   ///< Number of user + assistant messages
};

/// Full session payload including message history.
struct SessionData
{
   SessionInfo        info;
   QList<ChatMessage> history;
};

/// Manages chat-session persistence (create / save / load / delete).
///
/// Sessions are stored as individual JSON files in a configurable directory,
/// with a lightweight index file for fast listing.
class SessionManager : public QObject
{
   Q_OBJECT
public:
   explicit SessionManager(QObject* aParent = nullptr);
   ~SessionManager() override = default;

   // ---- Configuration ----

   /// Set the base directory used for session storage.
   /// If unset, falls back to QStandardPaths::AppDataLocation.
   void SetStorageDirectory(const QString& aDir);

   // ---- CRUD ----

   /// Create a new empty session and set it as current.
   SessionInfo CreateSession(const QString& aTitle = QString());

   /// Persist the given message history for session @a aSessionId.
   void SaveSession(const QString& aSessionId, const QList<ChatMessage>& aHistory);

   /// Load session data (info + history) by ID.  Returns empty data on failure.
   SessionData LoadSession(const QString& aSessionId);

   /// Return all known sessions, most-recently-updated first.
   QList<SessionInfo> ListSessions();

   /// Permanently delete a session by ID.
   void DeleteSession(const QString& aSessionId);

   /// Rename a session's title.
   void RenameSession(const QString& aSessionId, const QString& aNewTitle);

   /// Retrieve the title for a specific session (returns "New Chat" if not found).
   QString SessionTitle(const QString& aSessionId);

   // ---- Accessors ----

   QString CurrentSessionId() const { return mCurrentSessionId; }
   void    SetCurrentSessionId(const QString& aId) { mCurrentSessionId = aId; }

signals:
   void SessionListChanged();

private:
   // Storage helpers
   QString SessionDir() const;
   QString SessionFilePath(const QString& aSessionId) const;
   QString IndexFilePath() const;
   void    EnsureStorageDir();

   // Index management
   void LoadIndex();
   void SaveIndex();

public:
   // Auto-title generation
   static QString GenerateTitle(const QList<ChatMessage>& aHistory);

private:
   // JSON serialization
   static QJsonObject  MessageToJson(const ChatMessage& aMsg);
   static ChatMessage  MessageFromJson(const QJsonObject& aObj);
   static QJsonObject  SessionInfoToJson(const SessionInfo& aInfo);
   static SessionInfo  SessionInfoFromJson(const QJsonObject& aObj);

   // Data
   QString            mStorageDir;
   QString            mCurrentSessionId;
   QList<SessionInfo> mSessionIndex;
   bool               mIndexLoaded{false};
};

} // namespace AiChat

#endif // AICHAT_SESSION_MANAGER_HPP
