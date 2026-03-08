using Microsoft.Extensions.AI;

namespace FuXingAgent.Agents
{
    public enum WorkflowProgressKind
    {
        WorkflowStarted,
        StepStarted,
        StepFinished,
        WorkflowFinished
    }

    public sealed class WorkflowProgressEvent
    {
        public WorkflowProgressKind Kind { get; set; }
        public string WorkflowName { get; set; }
        public string WorkflowDisplayName { get; set; }
        public int StepIndex { get; set; }
        public int TotalSteps { get; set; }
        public string StepName { get; set; }
        public string Description { get; set; }
        public bool? Success { get; set; }
    }

    internal static class WorkflowProgressReporter
    {
        public static void StartWorkflow(string workflowName, string workflowDisplayName, int totalSteps)
        {
            Publish(new WorkflowProgressEvent
            {
                Kind = WorkflowProgressKind.WorkflowStarted,
                WorkflowName = workflowName,
                WorkflowDisplayName = workflowDisplayName,
                TotalSteps = totalSteps
            });
        }

        public static void StartStep(string workflowName, int stepIndex, int totalSteps, string stepName, string description = null)
        {
            Publish(new WorkflowProgressEvent
            {
                Kind = WorkflowProgressKind.StepStarted,
                WorkflowName = workflowName,
                StepIndex = stepIndex,
                TotalSteps = totalSteps,
                StepName = stepName,
                Description = description
            });
        }

        public static void FinishStep(string workflowName, int stepIndex, int totalSteps, string stepName, bool success, string description = null)
        {
            Publish(new WorkflowProgressEvent
            {
                Kind = WorkflowProgressKind.StepFinished,
                WorkflowName = workflowName,
                StepIndex = stepIndex,
                TotalSteps = totalSteps,
                StepName = stepName,
                Description = description,
                Success = success
            });
        }

        public static void FinishWorkflow(string workflowName, string workflowDisplayName, int totalSteps, bool success, string description = null)
        {
            Publish(new WorkflowProgressEvent
            {
                Kind = WorkflowProgressKind.WorkflowFinished,
                WorkflowName = workflowName,
                WorkflowDisplayName = workflowDisplayName,
                TotalSteps = totalSteps,
                Description = description,
                Success = success
            });
        }

        private static void Publish(WorkflowProgressEvent progressEvent)
        {
            ToolInvocationScope.CurrentOptions.Value?.ReportWorkflowProgress?.Invoke(progressEvent);
        }
    }

    public class ToolExecutionStartContent : AIContent
    {
        public string ToolName { get; }

        public ToolExecutionStartContent(string toolName)
        {
            ToolName = toolName;
        }
    }

    public class ToolExecutionEndContent : AIContent
    {
        public string ToolName { get; }
        public bool Success { get; }

        public ToolExecutionEndContent(string toolName, bool success)
        {
            ToolName = toolName;
            Success = success;
        }
    }

    public class AgentErrorContent : AIContent
    {
        public string Message { get; }

        public AgentErrorContent(string message)
        {
            Message = message;
        }
    }

    public sealed class WorkflowExecutionStartContent : AIContent
    {
        public string WorkflowName { get; }
        public string WorkflowDisplayName { get; }
        public int TotalSteps { get; }

        public WorkflowExecutionStartContent(string workflowName, string workflowDisplayName, int totalSteps)
        {
            WorkflowName = workflowName;
            WorkflowDisplayName = workflowDisplayName;
            TotalSteps = totalSteps;
        }
    }

    public sealed class WorkflowStepUpdateContent : AIContent
    {
        public string WorkflowName { get; }
        public int StepIndex { get; }
        public int TotalSteps { get; }
        public string StepName { get; }
        public string Description { get; }
        public bool IsCompleted { get; }
        public bool Success { get; }

        public WorkflowStepUpdateContent(
            string workflowName,
            int stepIndex,
            int totalSteps,
            string stepName,
            string description,
            bool isCompleted,
            bool success)
        {
            WorkflowName = workflowName;
            StepIndex = stepIndex;
            TotalSteps = totalSteps;
            StepName = stepName;
            Description = description;
            IsCompleted = isCompleted;
            Success = success;
        }
    }

    public sealed class WorkflowExecutionEndContent : AIContent
    {
        public string WorkflowName { get; }
        public string WorkflowDisplayName { get; }
        public int TotalSteps { get; }
        public bool Success { get; }
        public string Summary { get; }

        public WorkflowExecutionEndContent(string workflowName, string workflowDisplayName, int totalSteps, bool success, string summary)
        {
            WorkflowName = workflowName;
            WorkflowDisplayName = workflowDisplayName;
            TotalSteps = totalSteps;
            Success = success;
            Summary = summary;
        }
    }
}
