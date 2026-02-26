// -----------------------------------------------------------------------------
// File: AiChatPromptEngine.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PROMPT_ENGINE_HPP
#define AICHAT_PROMPT_ENGINE_HPP

#include <QString>
#include <QStringList>

namespace AiChat
{

/// Builds rich system prompts for the AI coding assistant, providing context
/// about the environment, project, and available tools.
class PromptEngine
{
public:
   PromptEngine() = default;

   /// Build the full system prompt including all sections.
   /// This is sent as the "system" message in the API request.
   QString BuildSystemPrompt() const;

   /// Build the system prompt for auto-condense (LLM summary).
   /// Instructs the LLM to produce a structured 10-section summary
   /// of the conversation history so far.
   QString BuildSummarizePrompt() const;

   /// Build the continuation prompt injected after condensation.
   /// Contains the summary and instructions for the LLM to continue.
   static QString BuildContinuationPrompt(const QString& aSummary);

   // ----------- Customization hooks -----------

   /// Set an additional custom instruction block from user preferences.
   void SetCustomInstructions(const QString& aInstructions) { mCustomInstructions = aInstructions; }

   /// Override the default persona / role description.
   void SetPersona(const QString& aPersona) { mPersona = aPersona; }

   /// Provide extra environment information (for dynamic context).
   void SetEnvironmentInfo(const QString& aInfo) { mEnvironmentInfo = aInfo; }

   /// Provide a catalog summary of available skills.
   void SetSkillCatalogSummary(const QString& aSummary) { mSkillCatalogSummary = aSummary; }

   /// Set an active skill context to inject into the prompt.
   void SetActiveSkillContent(const QString& aContent) { mActiveSkillContent = aContent; }

private:
   /// Build the role identity section (1 sentence).
   QString BuildRoleSection() const;

   /// Build the tone and style section (conciseness, objectivity).
   QString BuildToneSection() const;

   /// Build the capabilities section (what the assistant can do).
   QString BuildCapabilitiesSection() const;

   /// Build the objective section (how to approach a task).
   QString BuildObjectiveSection() const;

   /// Build the environment context section (OS, workspace, files).
   QString BuildEnvironmentSection() const;

   /// Build the skills section (catalog + active skill content).
   QString BuildSkillsSection() const;

   /// Build the tool usage strategy section.
   QString BuildToolRulesSection() const;

   /// Build the file-editing guidance section.
   QString BuildEditingFilesSection() const;

   /// Build the behavioural rules section.
   QString BuildRulesSection() const;

   // ----------- Member data -----------
   QString mCustomInstructions;
   QString mPersona;
   QString mEnvironmentInfo;
   QString mSkillCatalogSummary;
   QString mActiveSkillContent;
};

} // namespace AiChat

#endif // AICHAT_PROMPT_ENGINE_HPP
