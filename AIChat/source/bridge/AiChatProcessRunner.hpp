// -----------------------------------------------------------------------------
// File: AiChatProcessRunner.hpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#ifndef AICHAT_PROCESS_RUNNER_HPP
#define AICHAT_PROCESS_RUNNER_HPP

#include <QObject>
#include <QProcess>
#include <QString>

namespace AiChat
{

/// Result of a command execution
struct CommandResult
{
   int     exitCode{-1};
   QString stdOut;
   QString stdErr;
   bool    timedOut{false};
};

/// Runs external commands (e.g., WSF executable) asynchronously.
class ProcessRunner : public QObject
{
   Q_OBJECT
public:
   explicit ProcessRunner(QObject* aParent = nullptr);
   ~ProcessRunner() override;

   /// Run a shell command asynchronously (via cmd.exe on Windows, /bin/sh on Unix).
   /// @param aCommand    The command string to execute
   /// @param aWorkingDir Working directory (empty = project directory)
   /// @param aTimeoutMs  Timeout in milliseconds (0 = no timeout)
   void RunCommand(const QString& aCommand, const QString& aWorkingDir = {},
                   int aTimeoutMs = 30000);

   /// Run an executable directly without shell wrapping.
   /// Use this when the program path and arguments are known —
   /// avoids cmd.exe quoting issues entirely.
   void RunProgram(const QString& aProgram, const QStringList& aArgs,
                   const QString& aWorkingDir = {}, int aTimeoutMs = 30000);

   /// Cancel the running command.
   void Cancel();

   /// Check if a command is currently running.
   bool IsRunning() const;

signals:
   /// Emitted when stdout/stderr output is available (incrementally).
   void OutputReceived(const QString& aOutput);

   /// Emitted when the command has finished.
   void CommandFinished(const CommandResult& aResult);

private slots:
   void OnReadyReadStdout();
   void OnReadyReadStderr();
   void OnProcessFinished(int aExitCode, QProcess::ExitStatus aExitStatus);
   void OnProcessError(QProcess::ProcessError aError);
   void OnTimeout();

private:
   QProcess* mProcess;
   QString   mStdoutAccum;
   QString   mStderrAccum;
   bool      mTimedOut{false};
};

} // namespace AiChat

#endif // AICHAT_PROCESS_RUNNER_HPP
