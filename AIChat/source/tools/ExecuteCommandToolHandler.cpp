// -----------------------------------------------------------------------------
// File: ExecuteCommandToolHandler.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "ExecuteCommandToolHandler.hpp"
#include "../bridge/AiChatProjectBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"

#include <QEventLoop>
#include <QStringList>
#include <QRegularExpression>

namespace AiChat
{

namespace
{
#ifdef Q_OS_WIN

/// Strip a leading "cmd /c " or "cmd.exe /c " prefix that the AI sometimes adds,
/// since ProcessRunner already wraps in cmd /c.
QString StripCmdPrefix(const QString& aCommand)
{
   static const QRegularExpression cmdPrefixRe(
      QStringLiteral("^\\s*(?:cmd(?:\\.exe)?\\s+/c\\s+)"),
      QRegularExpression::CaseInsensitiveOption);
   QString result = aCommand;
   // May be nested: cmd /c cmd /c dir  →  strip iteratively
   for (int i = 0; i < 3; ++i) {
      const QRegularExpressionMatch m = cmdPrefixRe.match(result);
      if (!m.hasMatch()) break;
      result = result.mid(m.capturedEnd());
   }
   return result.trimmed().isEmpty() ? aCommand : result;
}

/// Detect common PowerShell cmdlets and return true if the command looks like PowerShell.
bool LooksLikePowerShell(const QString& aCommand)
{
   // Extract the first "word" (the verb-noun cmdlet or alias)
   static const QRegularExpression firstWordRe(QStringLiteral("^\\s*(\\S+)"));
   const QRegularExpressionMatch m = firstWordRe.match(aCommand);
   if (!m.hasMatch()) return false;

   const QString first = m.captured(1).toLower();

   // Common PowerShell verb-noun patterns (Get-*, Set-*, New-*, Select-*, etc.)
   static const QRegularExpression psVerbNoun(
      QStringLiteral("^(get|set|new|remove|select|where|format|out|invoke|"
                     "test|write|read|start|stop|measure|sort|group|"
                     "export|import|convertto|convertfrom|foreach|copy|move)-"),
      QRegularExpression::CaseInsensitiveOption);
   if (psVerbNoun.match(first).hasMatch()) return true;

   // Well-known PowerShell aliases that clash with nothing in cmd.exe
   static const QStringList psAliases = {
      QStringLiteral("ls"), QStringLiteral("cat"), QStringLiteral("rm"),
      QStringLiteral("cp"), QStringLiteral("mv"), QStringLiteral("pwd"),
      QStringLiteral("wget"), QStringLiteral("curl"),
      QStringLiteral("foreach-object"), QStringLiteral("where-object"),
      QStringLiteral("select-object"), QStringLiteral("measure-object")};
   return psAliases.contains(first);
}

/// Wrap a PowerShell command so it runs correctly from cmd.exe.
QString WrapAsPowerShell(const QString& aCommand)
{
   // Escape double quotes for cmd /c "powershell ..."
   QString escaped = aCommand;
   escaped.replace(QStringLiteral("\""), QStringLiteral("\\\""));
   return QStringLiteral("powershell -NoProfile -ExecutionPolicy Bypass -Command \"%1\"")
      .arg(escaped);
}

/// Adapt a command so it runs correctly on Windows via cmd /c.
QString AdaptWindowsCommand(const QString& aCommand)
{
   // 1) Strip redundant cmd /c prefix
   QString cmd = StripCmdPrefix(aCommand);

   // 1b) Normalize common Unix-style mkdir -p for Windows cmd
   static const QRegularExpression mkdirPRe(
      QStringLiteral("^\\s*mkdir\\s+-p\\s+"),
      QRegularExpression::CaseInsensitiveOption);
   if (mkdirPRe.match(cmd).hasMatch())
   {
      cmd.replace(mkdirPRe, QStringLiteral("mkdir "));
   }

   // 2) head → Get-Content (Unix compat)
   const QRegularExpression headRe(QStringLiteral("^\\s*head\\s+-?(\\d+)\\s+(.+)$"),
                                   QRegularExpression::CaseInsensitiveOption);
   const QRegularExpressionMatch headMatch = headRe.match(cmd);
   if (headMatch.hasMatch())
   {
      const int lines = headMatch.captured(1).toInt();
      QString filePath = headMatch.captured(2).trimmed();
      if (!(filePath.startsWith('"') || filePath.startsWith('\''))) {
         filePath = QStringLiteral("\"") + filePath + QStringLiteral("\"");
      }
      return QStringLiteral("powershell -NoProfile -Command \"Get-Content -Path %1 -TotalCount %2\"")
         .arg(filePath)
         .arg(lines);
   }

   // 3) If the command looks like raw PowerShell, wrap it
   if (LooksLikePowerShell(cmd)) {
      return WrapAsPowerShell(cmd);
   }

   return cmd;
}
#endif

bool IsNoMatchCommand(const QString& aCommand)
{
   const QString cmd = aCommand.toLower();
   return cmd.contains(QStringLiteral("findstr")) ||
          cmd.contains(QStringLiteral("select-string")) ||
          cmd.contains(QStringLiteral("grep"));
}

bool IsNoMatchExit(const QString& aCommand, const CommandResult& aResult, const QString& aOutput)
{
   if (aResult.timedOut || aResult.exitCode != 1) {
      return false;
   }
   if (!aResult.stdErr.trimmed().isEmpty()) {
      return false;
   }
   if (!aOutput.trimmed().isEmpty()) {
      return false;
   }
   return IsNoMatchCommand(aCommand);
}

/// Return true if the command invokes mission.exe (the AFSIM simulation runner).
bool IsMissionExeCommand(const QString& aCommand)
{
   return aCommand.contains(QStringLiteral("mission.exe"), Qt::CaseInsensitive);
}

/// Detect when mission.exe ran and the scenario loaded + simulation started,
/// even though the process crashed with a Windows STATUS_xxx code
/// (e.g. DDS Gateway heap corruption, STATUS_HEAP_CORRUPTION = 0xC0000374).
/// In this case the scenario itself is valid; the crash is a runtime/plugin issue.
bool IsMissionSimulationSuccess(const QString& aCommand,
                                const CommandResult& aResult,
                                const QString& aOutput)
{
   if (!IsMissionExeCommand(aCommand)) {
      return false;
   }

   // Windows STATUS_xxx crash codes are very large negative signed integers
   // (STATUS_HEAP_CORRUPTION = -1073740940, STATUS_ACCESS_VIOLATION = -1073741819, ...)
   const bool isWindowsCrash = (aResult.exitCode < -1000000000);
   if (!isWindowsCrash) {
      return false;
   }

   // Scenario loaded and simulation started — the crash is post-load (plugin issue)
   const bool simStarted = aOutput.contains(QStringLiteral("Starting simulation"));
   const bool simFailed  = aOutput.contains(QStringLiteral("Could not process input files")) ||
                           aOutput.contains(QStringLiteral("Reading of simulation input failed"));

   return simStarted && !simFailed;
}

int ParseTimeoutMs(const QJsonObject& aParams, const char* aKey, int aDefault)
{
   const QJsonValue timeoutVal = aParams.value(aKey);
   return timeoutVal.isUndefined() ? aDefault : qMax(0, qRound(timeoutVal.toVariant().toDouble()));
}

bool ResolveCommandInvocation(const QString& aCommandRaw,
                              const QString& aWorkingDirRaw,
                              PathAccessManager* aPathAccess,
                              bool aStrictResolve,
                              QString& aCommand,
                              QString& aWorkingDir,
                              QString& aError)
{
   aCommand = aCommandRaw;
   aWorkingDir = aWorkingDirRaw;
   aError.clear();

   if (!aPathAccess) {
      return true;
   }

   const auto resolved = aPathAccess->ResolveCommand(aCommandRaw, aWorkingDirRaw);
   if (!resolved.ok)
   {
      if (aStrictResolve)
      {
         aError = resolved.error;
         return false;
      }
      return true;
   }

   aCommand = resolved.command;
   if (!resolved.workingDir.isEmpty()) {
      aWorkingDir = resolved.workingDir;
   }
   return true;
}

ToolResult BuildExecuteCommandResult(const QString& aCommand,
                                     const CommandResult& aResult,
                                     int aTimeoutMs)
{
   QString output;
   if (!aResult.stdOut.isEmpty()) {
      output += aResult.stdOut;
   }
   if (!aResult.stdErr.isEmpty()) {
      if (!output.isEmpty()) {
         output += '\n';
      }
      output += QStringLiteral("[stderr]\n") + aResult.stdErr;
   }

   const int maxOutputLen = 8000;
   if (output.size() > maxOutputLen) {
      const int halfLen = maxOutputLen / 2;
      const QString head = output.left(halfLen);
      const QString tail = output.right(halfLen);
      output = head +
               QStringLiteral("\n\n... [%1 characters omitted] ...\n\n").arg(output.size() - maxOutputLen) +
               tail;
   }

   if (aResult.timedOut) {
      output += QStringLiteral("\n\n[Command was terminated after %1 ms timeout. "
                               "The process may have been running successfully but exceeded the time limit. "
                               "If the output above shows the expected results, consider increasing timeout_ms. "
                               "Actual exit code %2 is from forced termination, not the command itself.]"
                              ).arg(aTimeoutMs).arg(aResult.exitCode);
   }

   const QString summary = QStringLiteral("Command: %1\nExit code: %2%3")
                              .arg(aCommand)
                              .arg(aResult.timedOut ? QStringLiteral("N/A (killed by timeout)") : QString::number(aResult.exitCode))
                              .arg(aResult.timedOut
                                      ? QStringLiteral(" — process was forcefully terminated after %1 ms").arg(aTimeoutMs)
                                      : QString());

   bool success = (aResult.exitCode == 0 && !aResult.timedOut);
   const bool noMatches = IsNoMatchExit(aCommand, aResult, output);
   if (!success && noMatches) {
      success = true;
      if (output.trimmed().isEmpty()) {
         output = QStringLiteral("[No matches found]");
      }
   }

   const bool missionCrashOk = !success && !aResult.timedOut &&
                               IsMissionSimulationSuccess(aCommand, aResult, output);
   if (missionCrashOk) {
      success = true;
      output += QStringLiteral("\n\n[NOTE: Process crashed with exit code %1, likely due to a "
                               "plugin issue (e.g. DDS Gateway), not a scenario error. "
                               "The scenario loaded and simulation started successfully.]")
                   .arg(aResult.exitCode);
   }

   const bool missionTimeoutOk = !success && aResult.timedOut &&
                                 IsMissionExeCommand(aCommand) &&
                                 output.contains(QStringLiteral("Starting simulation"));
   if (missionTimeoutOk) {
      success = true;
   }

   const QString statusLabel = aResult.timedOut
      ? QStringLiteral("Executed: %1 (terminated after %2 ms timeout — check output to determine if command actually succeeded)").arg(aCommand).arg(aTimeoutMs)
      : noMatches
         ? QStringLiteral("Executed: %1 (no matches)").arg(aCommand)
         : QStringLiteral("Executed: %1 (exit code %2)").arg(aCommand).arg(aResult.exitCode);

   return {success,
           QStringLiteral("%1\n\n%2").arg(summary, output),
           statusLabel,
           false};
}
} // namespace

ExecuteCommandToolHandler::ExecuteCommandToolHandler(ProcessRunner* aRunner,
                                                     PathAccessManager* aPathAccess)
   : mRunner(aRunner)
   , mPathAccess(aPathAccess)
{
}

ToolDefinition ExecuteCommandToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("execute_command");
   def.description = QStringLiteral(
      "Execute a shell command on the system. "
      "Use this to run build commands, scripts, or other system tools. "
      "The command will be executed in the workspace root by default. "
      "Output (stdout + stderr) will be returned. "
      "Long-running commands will be terminated after the timeout.");
   def.parameters = {
      {"command", "string", "The shell command to execute", true},
      {"working_dir", "string",
         "The working directory for the command. Default is the primary workspace root. "
         "Use @workspace:path to target a specific root.", false},
      {"timeout_ms", "integer",
       "Timeout in milliseconds. Default is 30000 (30 seconds). Use 0 for no timeout.", false}};
   return def;
}

bool ExecuteCommandToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!aParams.contains("command") || aParams["command"].toString().trimmed().isEmpty()) {
      aError = QStringLiteral("Missing required parameter: command");
      return false;
   }

   // Block obviously dangerous commands
   const QString cmd = aParams["command"].toString().trimmed().toLower();
   static const QStringList dangerousPatterns = {
      "rm -rf /", "format c:", "del /f /s /q c:", "rmdir /s /q c:"};
   for (const auto& pattern : dangerousPatterns) {
      if (cmd.contains(pattern)) {
         aError = QStringLiteral("Blocked potentially dangerous command: %1").arg(cmd);
         return false;
      }
   }

   return true;
}

ToolResult ExecuteCommandToolHandler::Execute(const QJsonObject& aParams)
{
   if (!mRunner) {
      return {false, QStringLiteral("Error: Process runner not available."), {}, false};
   }

   const QString commandRaw = aParams["command"].toString().trimmed();
   QString       workingDir = aParams.value("working_dir").toString();
   const int     timeoutMs  = ParseTimeoutMs(aParams, "timeout_ms", 30000);

   QString command;
   QString resolveError;
   if (!ResolveCommandInvocation(commandRaw, workingDir, mPathAccess, true,
                                 command, workingDir, resolveError)) {
      return {false, QStringLiteral("Error: %1").arg(resolveError), {}, false};
   }

#ifdef Q_OS_WIN
   const QString adaptedCommand = AdaptWindowsCommand(command);
#else
   const QString adaptedCommand = command;
#endif

   // Default working directory to workspace root
   if (workingDir.isEmpty()) {
      workingDir = ProjectBridge::GetWorkspaceRoot();
   }

   // Execute the command and wait synchronously using a local event loop
   QEventLoop    loop;
   CommandResult cmdResult;

   auto conn = QObject::connect(
      mRunner, &ProcessRunner::CommandFinished,
      [&loop, &cmdResult](const CommandResult& aResult) {
         cmdResult = aResult;
         loop.quit();
      });

   mRunner->RunCommand(adaptedCommand, workingDir, timeoutMs);
   loop.exec();

   QObject::disconnect(conn);

   return BuildExecuteCommandResult(adaptedCommand, cmdResult, timeoutMs);
}

void ExecuteCommandToolHandler::ExecuteAsync(const QJsonObject& aParams,
                                             std::function<void(ToolResult)> aOnComplete)
{
   if (!mRunner) {
      aOnComplete({false, QStringLiteral("Error: Process runner not available."), {}, false});
      return;
   }

   const QString commandRaw = aParams["command"].toString().trimmed();
   QString       workingDir = aParams.value("working_dir").toString();
   const int     timeoutMs  = ParseTimeoutMs(aParams, "timeout_ms", 30000);

   QString command;
   QString resolveError;
   if (!ResolveCommandInvocation(commandRaw, workingDir, mPathAccess, true,
                                 command, workingDir, resolveError)) {
      aOnComplete({false, QStringLiteral("Error: %1").arg(resolveError), {}, false});
      return;
   }

#ifdef Q_OS_WIN
   const QString adaptedCommand = AdaptWindowsCommand(command);
#else
   const QString adaptedCommand = command;
#endif

   if (workingDir.isEmpty()) {
      workingDir = ProjectBridge::GetWorkspaceRoot();
   }

   auto conn = std::make_shared<QMetaObject::Connection>();
   *conn = QObject::connect(
      mRunner, &ProcessRunner::CommandFinished,
      [this, aOnComplete, adaptedCommand, timeoutMs, conn](const CommandResult& aResult) {
         QObject::disconnect(*conn);

         aOnComplete(BuildExecuteCommandResult(adaptedCommand, aResult, timeoutMs));
      });

   mRunner->RunCommand(adaptedCommand, workingDir, timeoutMs);
}

QString ExecuteCommandToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString commandRaw = aParams["command"].toString().trimmed();
   const QString workingDirRaw = aParams.value("working_dir").toString().trimmed();
   const int     timeoutMs  = ParseTimeoutMs(aParams, "timeout_ms", 30000);

   QString command;
   QString workingDir = workingDirRaw;
   QString unusedError;
   ResolveCommandInvocation(commandRaw, workingDirRaw, mPathAccess, false,
                            command, workingDir, unusedError);

#ifdef Q_OS_WIN
   const QString previewCommand = AdaptWindowsCommand(command);
#else
   const QString previewCommand = command;
#endif

   QStringList lines;
   lines << QStringLiteral("Command:")
         << previewCommand;
   if (!workingDir.isEmpty()) {
      lines << QStringLiteral("Working directory:")
            << workingDir;
   }
   lines << QStringLiteral("Timeout: %1 ms").arg(timeoutMs);
   return lines.join('\n');
}

} // namespace AiChat
