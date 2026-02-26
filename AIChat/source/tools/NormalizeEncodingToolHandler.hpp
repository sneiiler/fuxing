// -----------------------------------------------------------------------------
// File: NormalizeEncodingToolHandler.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-12
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_NORMALIZE_ENCODING_TOOL_HANDLER_HPP
#define AICHAT_NORMALIZE_ENCODING_TOOL_HANDLER_HPP

#include "IToolHandler.hpp"

namespace AiChat
{

/// Scan or normalize workspace file encodings.
class NormalizeEncodingToolHandler : public IToolHandler
{
public:
   ToolDefinition GetDefinition() const override;
   bool ValidateParams(const QJsonObject& aParams, QString& aError) const override;
   ToolResult Execute(const QJsonObject& aParams) override;
   bool RequiresApproval() const override { return true; }
};

} // namespace AiChat

#endif // AICHAT_NORMALIZE_ENCODING_TOOL_HANDLER_HPP
