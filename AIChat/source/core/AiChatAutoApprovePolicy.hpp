// -----------------------------------------------------------------------------
// File: AiChatAutoApprovePolicy.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_AUTO_APPROVE_POLICY_HPP
#define AICHAT_AUTO_APPROVE_POLICY_HPP

#include <QJsonObject>
#include <QString>

namespace AiChat
{

class PrefObject;
class PathAccessManager;

class AutoApprovePolicy
{
public:
   AutoApprovePolicy(PrefObject* aPrefs, PathAccessManager* aPathAccess);

   bool RequiresApproval(const QString& aToolName, const QJsonObject& aParams) const;

private:
   bool IsCommandSafe(const QString& aCommand) const;

   PrefObject* mPrefs{nullptr};
   PathAccessManager* mPathAccess{nullptr};
};

} // namespace AiChat

#endif // AICHAT_AUTO_APPROVE_POLICY_HPP
