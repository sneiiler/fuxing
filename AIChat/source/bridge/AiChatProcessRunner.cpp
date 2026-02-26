// -----------------------------------------------------------------------------
// File: AiChatProcessRunner.cpp
// Author: Yin, Kaifeng
// Email: ykf@live.cn
// Date: 2026-02-10
// Version: 1.0.0
// Copyright (c) 2026 Yin, Kaifeng. All rights reserved.
// -----------------------------------------------------------------------------

#include "AiChatProcessRunner.hpp"

#include <QTimer>

namespace AiChat
{

ProcessRunner::ProcessRunner(QObject* aParent)
   : QObject(aParent)
   , mProcess(new QProcess(this))
{
   connect(mProcess, &QProcess::readyReadStandardOutput,
           this, &ProcessRunner::OnReadyReadStdout);
   connect(mProcess, &QProcess::readyReadStandardError,
           this, &ProcessRunner::OnReadyReadStderr);
   connect(mProcess, QOverload<int, QProcess::ExitStatus>::of(&QProcess::finished),
           this, &ProcessRunner::OnProcessFinished);
   connect(mProcess, &QProcess::errorOccurred,
           this, &ProcessRunner::OnProcessError);
}

ProcessRunner::~ProcessRunner()
{
   Cancel();
}

void ProcessRunner::RunCommand(const QString& aCommand,
                               const QString& aWorkingDir,
                               int            aTimeoutMs)
{
   if (IsRunning()) {
      Cancel();
   }

   mStdoutAccum.clear();
   mStderrAccum.clear();
   mTimedOut = false;

   if (!aWorkingDir.isEmpty()) {
      mProcess->setWorkingDirectory(aWorkingDir);
   }

   // Set up timeout
   if (aTimeoutMs > 0) {
      QTimer::singleShot(aTimeoutMs, this, &ProcessRunner::OnTimeout);
   }

   // On Windows, use cmd /c to interpret the command.
   // IMPORTANT: Use setNativeArguments() to bypass Qt's MSVC-style argument
   // quoting which escapes " inside arguments as \".  cmd.exe treats \\
   // as a literal backslash, not an escape, so \"path\" breaks cmd parsing.
#ifdef Q_OS_WIN
   mProcess->setProgram(QStringLiteral("cmd.exe"));
   mProcess->setNativeArguments(QStringLiteral("/c %1").arg(aCommand));
   mProcess->start();
#else
   mProcess->start("/bin/sh", QStringList() << "-c" << aCommand);
#endif
}

void ProcessRunner::RunProgram(const QString& aProgram,
                               const QStringList& aArgs,
                               const QString& aWorkingDir,
                               int            aTimeoutMs)
{
   if (IsRunning()) {
      Cancel();
   }

   mStdoutAccum.clear();
   mStderrAccum.clear();
   mTimedOut = false;

   if (!aWorkingDir.isEmpty()) {
      mProcess->setWorkingDirectory(aWorkingDir);
   }

   if (aTimeoutMs > 0) {
      QTimer::singleShot(aTimeoutMs, this, &ProcessRunner::OnTimeout);
   }

   // Clear any native arguments left over from a previous RunCommand() call.
   // On Windows, QProcess::setNativeArguments() persists across start() calls;
   // if RunCommand() was called before, its "/c <cmd>" would leak into the
   // argument list of the directly-launched program (e.g. mission.exe receives
   // "/c" as an unexpected argument).
   mProcess->setNativeArguments(QString());
   mProcess->start(aProgram, aArgs);
}

void ProcessRunner::Cancel()
{
   if (IsRunning()) {
      mProcess->kill();
      mProcess->waitForFinished(3000);
   }
}

bool ProcessRunner::IsRunning() const
{
   return mProcess->state() != QProcess::NotRunning;
}

void ProcessRunner::OnReadyReadStdout()
{
   QString output = QString::fromUtf8(mProcess->readAllStandardOutput());
   mStdoutAccum += output;
   emit OutputReceived(output);
}

void ProcessRunner::OnReadyReadStderr()
{
   QString errOutput = QString::fromUtf8(mProcess->readAllStandardError());
   mStderrAccum += errOutput;
   emit OutputReceived(errOutput);
}

void ProcessRunner::OnProcessFinished(int aExitCode, QProcess::ExitStatus /*aExitStatus*/)
{
   // Read any remaining output
   OnReadyReadStdout();
   OnReadyReadStderr();

   CommandResult result;
   result.exitCode  = aExitCode;
   result.stdOut    = mStdoutAccum;
   result.stdErr    = mStderrAccum;
   result.timedOut  = mTimedOut;

   emit CommandFinished(result);
}

void ProcessRunner::OnProcessError(QProcess::ProcessError aError)
{
   if (aError == QProcess::FailedToStart) {
      CommandResult result;
      result.exitCode = -1;
      result.stdErr   = QStringLiteral("Failed to start process: ") + mProcess->errorString();
      result.timedOut = false;
      emit CommandFinished(result);
   }
   // Other errors (crashed, timedout) will be handled in OnProcessFinished
}

void ProcessRunner::OnTimeout()
{
   if (IsRunning()) {
      mTimedOut = true;
      Cancel();
   }
}

} // namespace AiChat
