using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Checks conveyor setups for mistakes that otherwise only show up at runtime.
    /// Used by the conveyor inspector, the Scene view overlay and Tools > Conveyor System > Validate Scene.
    /// </summary>
    public static class ConveyorValidator
    {
        /// <summary>
        /// One validation finding.
        /// </summary>
        public struct Issue
        {
            public MessageType Severity;
            public string Message;

            public Issue(MessageType severity, string message)
            {
                Severity = severity;
                Message = message;
            }
        }

        /// <summary>
        /// Validates a single conveyor's own setup (points, triggers, type-specific fields).
        /// </summary>
        public static List<Issue> Validate(ConveyorBase c)
        {
            var issues = new List<Issue>();
            if (c == null) return issues;

            bool isSource = c is UnloaderConveyor;
            bool isSink = c is LoaderConveyor;
            int outputCount = c.OutputPoints != null ? c.OutputPoints.Length : 0;

            // Points
            if (!HasAny(ConveyorSnapping.GetInputPoints(c)))
                issues.Add(new Issue(MessageType.Error, "Input Point is not assigned."));
            if (c.CenterPoint == null)
                issues.Add(new Issue(MessageType.Error, "Center Point is not assigned."));
            if (outputCount == 0 && !isSink)
                issues.Add(new Issue(MessageType.Warning, "No Output Points: parcels can never leave this conveyor."));
            for (int i = 0; i < outputCount; i++)
                if (c.OutputPoints[i] == null)
                    issues.Add(new Issue(MessageType.Error, $"Output Points[{i}] is not assigned."));
            if (c is JoinerConveyor joiner && joiner.InputPoints != null)
                for (int i = 0; i < joiner.InputPoints.Length; i++)
                    if (joiner.InputPoints[i] == null)
                        issues.Add(new Issue(MessageType.Warning, $"Input Points[{i}] is not assigned."));

            if (c.Speed <= 0f)
                issues.Add(new Issue(MessageType.Error, "Speed must be greater than 0."));

            // Triggers
            var triggers = c.GetComponentsInChildren<ConveyorTrigger>(true);
            bool hasInputTrigger = false;
            var outputsWithTrigger = new HashSet<int>();
            foreach (var t in triggers)
            {
                var col = t.GetComponent<Collider>();
                if (col == null)
                    issues.Add(new Issue(MessageType.Error, $"Trigger '{t.name}' has no Collider."));
                else if (!col.isTrigger)
                    issues.Add(new Issue(MessageType.Error, $"Collider on '{t.name}' must have Is Trigger enabled."));

                if (!t.IsOutput)
                {
                    hasInputTrigger = true;
                    continue;
                }

                if (t.OutputIndex < 0 || t.OutputIndex >= outputCount)
                    issues.Add(new Issue(MessageType.Error, $"Output trigger '{t.name}' uses Output Index {t.OutputIndex}, but there are only {outputCount} Output Points."));
                else
                    outputsWithTrigger.Add(t.OutputIndex);
            }

            if (!hasInputTrigger && !isSource)
                issues.Add(new Issue(MessageType.Warning, "No input trigger (ConveyorTrigger with Is Output off): other conveyors cannot connect to this one."));

            if (!isSink)
                for (int i = 0; i < outputCount; i++)
                    if (!outputsWithTrigger.Contains(i))
                        issues.Add(new Issue(MessageType.Warning, $"Output Points[{i}] has no output trigger with Output Index {i}: that exit can never connect."));

            if (triggers.Length > 0 && c.GetComponentInChildren<Rigidbody>(true) == null)
                issues.Add(new Issue(MessageType.Info, "No Rigidbody on this conveyor's triggers. Unity only reports trigger overlaps if at least one side has a Rigidbody, so neighbours must provide one."));

            // Type-specific
            if (c is SmartSplitterConveyor smart)
            {
                if (smart.DefaultOutputIndex < 0 || smart.DefaultOutputIndex >= outputCount)
                    issues.Add(new Issue(MessageType.Warning, $"Default Output Index {smart.DefaultOutputIndex} does not exist (outputs: {outputCount})."));
                if (smart.TypeToOutput != null)
                    for (int i = 0; i < smart.TypeToOutput.Length; i++)
                        if (smart.TypeToOutput[i] < 0 || smart.TypeToOutput[i] >= outputCount)
                            issues.Add(new Issue(MessageType.Warning, $"Type To Output[{i}] ({(ParcelType)i}) points to output {smart.TypeToOutput[i]}, which does not exist."));
            }

            if (c is LoaderConveyor loader && loader.TargetStorage == null)
                issues.Add(new Issue(MessageType.Info, "No Target Storage: parcels reaching this Loader are consumed (deactivated)."));

            if (c is UnloaderConveyor unloader && unloader.ParcelStorage == null)
                issues.Add(new Issue(MessageType.Warning, "No Parcel Storage assigned: this Unloader will never produce parcels."));

            return issues;
        }

        /// <summary>
        /// Validates every conveyor in the open scenes and logs the findings, plus a summary.
        /// </summary>
        [MenuItem("Tools/Conveyor System/Validate Scene", priority = 100)]
        public static void ValidateScene()
        {
            var conveyors = SceneQuery.FindAll<ConveyorBase>(includeInactive: true);
            int errors = 0, warnings = 0;

            foreach (var c in conveyors)
            {
                foreach (var issue in Validate(c))
                {
                    string msg = $"[Conveyor System] {c.name}: {issue.Message}";
                    if (issue.Severity == MessageType.Error) { errors++; Debug.LogError(msg, c); }
                    else if (issue.Severity == MessageType.Warning) { warnings++; Debug.LogWarning(msg, c); }
                    else Debug.Log(msg, c);
                }
            }

            int open = 0, nearMiss = 0;
            foreach (var link in ConveyorConnections.Collect())
            {
                if (link.State == ConveyorConnections.LinkState.Open) open++;
                else if (link.State == ConveyorConnections.LinkState.NearMiss)
                {
                    nearMiss++;
                    Debug.LogWarning($"[Conveyor System] {link.From.name}: output '{link.FromTrigger.name}' is close to '{link.To.name}' but its triggers do not overlap, so they will not connect.", link.From);
                }
            }
            warnings += nearMiss;

            string summary = $"[Conveyor System] Validated {conveyors.Length} conveyors: {errors} error(s), {warnings} warning(s), {open} open output(s).";
            if (errors > 0) Debug.LogError(summary);
            else if (warnings > 0) Debug.LogWarning(summary);
            else Debug.Log(summary);
        }

        private static bool HasAny<T>(IEnumerable<T> items)
        {
            foreach (var _ in items) return true;
            return false;
        }
    }
}
