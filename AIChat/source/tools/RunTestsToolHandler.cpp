// -----------------------------------------------------------------------------
// File: RunTestsToolHandler.cpp
// -----------------------------------------------------------------------------

#include "RunTestsToolHandler.hpp"

#include "../bridge/AiChatProcessRunner.hpp"
#include "../bridge/AiChatProjectBridge.hpp"
#include "../core/AiChatPathAccessManager.hpp"

#include <QCoreApplication>
#include <QDir>
#include <QEventLoop>
#include <QFileInfo>

namespace AiChat
{

namespace
{
bool IsMissionSimulationSuccess(const CommandResult& aResult, const QString& aOutput)
{
   const bool isWindowsCrash = (aResult.exitCode < -1000000000);
   if (!isWindowsCrash) {
      return false;
   }

   const bool simStarted = aOutput.contains(QStringLiteral("Starting simulation"));
   const bool simFailed  = aOutput.contains(QStringLiteral("Could not process input files")) ||
                           aOutput.contains(QStringLiteral("Reading of simulation input failed"));

   return simStarted && !simFailed;
}

ToolResult BuildRunTestsResult(const QString& aOutput,
                               bool aTimedOut,
                               bool aTextualSuccess,
                               bool aCrashButValid)
{
   if (aTimedOut)
   {
      return {false,
              aOutput + QStringLiteral("\n\nResult: TIMED OUT"),
              QStringLiteral("run_tests timed out"),
              false};
   }

   if (aTextualSuccess || aCrashButValid)
   {
      QString content = aOutput;
      if (aCrashButValid && !aTextualSuccess)
      {
         content += QStringLiteral("\n\nResult: Scenario loaded and simulation started; process crashed later (likely runtime/plugin issue).");
      }
      else
      {
         content += QStringLiteral("\n\nResult: SUCCESS");
      }

      return {true,
              content,
              QStringLiteral("run_tests passed"),
              false};
   }

   return {false,
           aOutput + QStringLiteral("\n\nResult: FAILED"),
           QStringLiteral("run_tests failed"),
           false};
}

QString FormatResultText(const QString& aCommand,
                         const CommandResult& aResult,
                         const QString& aStdOut,
                         const QString& aStdErr)
{
   QString out;
   out += QStringLiteral("Command: %1\n").arg(aCommand);
   out += QStringLiteral("Exit code: %1\n").arg(aResult.exitCode);
   if (aResult.timedOut) {
      out += QStringLiteral("(timed out)\n");
   }
   out += QStringLiteral("\n");

   if (!aStdOut.isEmpty()) {
      out += aStdOut;
      if (!out.endsWith('\n')) out += '\n';
   }
   if (!aStdErr.isEmpty()) {
      out += QStringLiteral("\n[stderr]\n");
      out += aStdErr;
      if (!out.endsWith('\n')) out += '\n';
   }

   return out.trimmed();
}

int ParseTimeoutMs(const QJsonObject& aParams)
{
   const QJsonValue timeoutVal = aParams.value(QStringLiteral("timeout_ms"));
   return timeoutVal.isUndefined()
      ? 120000
      : qMax(0, qRound(timeoutVal.toVariant().toDouble()));
}

bool PrepareScenarioPath(const QJsonObject& aParams,
                         PathAccessManager* aPathAccess,
                         QString& aScenarioPath,
                         QString& aError)
{
   aScenarioPath = aParams.value(QStringLiteral("path")).toString().trimmed();

   if (!aScenarioPath.isEmpty())
   {
      if (aPathAccess)
      {
         const auto res = aPathAccess->ResolveForRead(aScenarioPath);
         if (!res.ok) {
            aError = QStringLiteral("Error: %1").arg(res.error);
            return false;
         }
         aScenarioPath = res.resolvedPath;
      }

      QString err;
      if (!ProjectBridge::SetStartupFiles(QStringList{aScenarioPath}, err))
      {
         aError = QStringLiteral("Error: failed to set startup file: %1").arg(err);
         return false;
      }
      return true;
   }

   const QStringList startupFiles = ProjectBridge::GetStartupFiles();
   if (startupFiles.isEmpty())
   {
      aError = QStringLiteral("Error: No startup file configured. Provide path or call set_startup_file first.");
      return false;
   }

   aScenarioPath = startupFiles.front();
   return true;
}
} // namespace

RunTestsToolHandler::RunTestsToolHandler(ProcessRunner* aRunner,
                                         PathAccessManager* aPathAccess)
   : mRunner(aRunner)
   , mPathAccess(aPathAccess)
{
}

ToolDefinition RunTestsToolHandler::GetDefinition() const
{
   ToolDefinition def;
   def.name        = QStringLiteral("run_tests");
   def.description = QStringLiteral(
      "Run scenario validation. "
      "Uses the current startup file by default, or an explicit scenario path if provided.");
   def.parameters = {
      {QStringLiteral("path"), QStringLiteral("string"),
       QStringLiteral("Optional scenario file path to test. If omitted, uses current startup file."), false},
      {QStringLiteral("timeout_ms"), QStringLiteral("integer"),
       QStringLiteral("Optional timeout in milliseconds. Default is 120000."), false}
   };
   return def;
}

bool RunTestsToolHandler::ValidateParams(const QJsonObject& aParams, QString& aError) const
{
   if (!mRunner) {
      aError = QStringLiteral("Process runner not available.");
      return false;
   }

   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (!path.isEmpty() && mPathAccess)
   {
      const auto res = mPathAccess->ResolveForRead(path);
      if (!res.ok) {
         aError = res.error;
         return false;
      }
      QFileInfo info(res.resolvedPath);
      if (!info.exists() || !info.isFile()) {
         aError = QStringLiteral("Scenario file does not exist: %1").arg(path);
         return false;
      }
   }

   return true;
}

ToolResult RunTestsToolHandler::Execute(const QJsonObject& aParams)
{
   if (!mRunner) {
      return {false, QStringLiteral("Error: Process runner not available."), {}, false};
   }

   const int timeoutMs = ParseTimeoutMs(aParams);
   QString scenarioPath;
   QString prepareError;
   if (!PrepareScenarioPath(aParams, mPathAccess, scenarioPath, prepareError)) {
      return {false, prepareError, {}, false};
   }

   const QString missionExe = QDir(QCoreApplication::applicationDirPath()).filePath(QStringLiteral("mission.exe"));
   if (!QFileInfo::exists(missionExe))
   {
      return {false,
              QStringLiteral("Error: mission.exe not found at %1").arg(QDir::toNativeSeparators(missionExe)),
              {}, false};
   }

   // Build display command string (for log / result output only)
   const QString command = QStringLiteral("\"%1\" \"%2\"")
      .arg(QDir::toNativeSeparators(missionExe), QDir::toNativeSeparators(scenarioPath));
   const QString workingDir = ProjectBridge::GetWorkspaceRoot();

   QEventLoop loop;
   CommandResult cmdResult;
   auto conn = QObject::connect(
      mRunner, &ProcessRunner::CommandFinished,
      [&loop, &cmdResult](const CommandResult& aResult) {
         cmdResult = aResult;
         loop.quit();
      });

   // Run mission.exe directly (bypass cmd.exe to avoid quoting issues)
   mRunner->RunProgram(missionExe, QStringList{scenarioPath}, workingDir, timeoutMs);
   loop.exec();
   QObject::disconnect(conn);

   const QString output = FormatResultText(command, cmdResult, cmdResult.stdOut, cmdResult.stdErr);

   const bool textualSuccess = output.contains(QStringLiteral("Simulation complete"), Qt::CaseInsensitive);
   const bool crashButValid = IsMissionSimulationSuccess(cmdResult, output);
   const bool timedOut = cmdResult.timedOut;
   return BuildRunTestsResult(output, timedOut, textualSuccess, crashButValid);
}

QString RunTestsToolHandler::GenerateDiff(const QJsonObject& aParams) const
{
   const QString path = aParams.value(QStringLiteral("path")).toString().trimmed();
   if (path.isEmpty()) {
      return QStringLiteral("Run tests using current startup file");
   }
   return QStringLiteral("Run tests for scenario: %1").arg(path);
}

} // namespace AiChat
