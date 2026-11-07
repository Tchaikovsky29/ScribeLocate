using System.Collections;
using OpenQA.Selenium;

internal static class ContextExplorer
{
    private const int MaxDepth = 10;
    private const int MaxContexts = 96;

    internal sealed record Match(IWebElement Element, string Path, IReadOnlyList<IWebElement> FrameChain);
    private sealed record Candidate(Match Match, int Depth, bool HeadingMatch, bool LabelMatch);

    private sealed class ScanState
    {
        public readonly List<Candidate> Candidates = [];
        public int Contexts;
        public readonly List<string> SlotAssignments = [];
        public int RepeatedUrls;
        public int Skipped;
        public readonly List<string> Errors = [];
    }

    public static string LastScanSummary { get; private set; } = "";

    public static Match? Find(IWebDriver driver, string? headingHint)
    {
        driver.SwitchTo().DefaultContent();
        var heading = headingHint ?? "Form";

        // The practice page deliberately duplicates forms in nested contexts.
        // Its ordinary top-level form is the default when it is unambiguous.
        var topLevel = SemanticForm.VisibleForms(driver)
            .Where(form => SemanticForm.HasHeading(form, heading)).ToList();
        if (topLevel.Count > 1)
            throw new InvalidOperationException($"{topLevel.Count} top-level forms have heading '{heading}'");
        if (topLevel.Count == 1)
        {
            LastScanSummary = "selected one top-level form";
            return new Match(topLevel[0], $"document > form[heading=\"{heading}\"]", []);
        }

        var state = new ScanState();
        var url = CurrentUrl(driver);
        try
        {
            VisitRoot(driver, null, "document", [], new HashSet<string>(StringComparer.Ordinal) { url }, 0,
                heading, headingHint is null, state);
        }
        finally
        {
            driver.SwitchTo().DefaultContent();
        }

        LastScanSummary = $"explored {state.Contexts} contexts, found {state.SlotAssignments.Count} slot assignments, " +
            $"skipped {state.RepeatedUrls} repeated-URL frames and {state.Skipped} unavailable or bounded contexts" +
            (state.Errors.Count > 0 ? $" ({string.Join("; ", state.Errors.Take(2))})" : "");

        var preferred = state.Candidates.Where(c => c.HeadingMatch).ToList();
        var pool = preferred.Count > 0 ? preferred : state.Candidates.Where(c => c.LabelMatch).ToList();
        if (pool.Count == 0) return null;

        // A shallower form is the page's primary example. Never choose by sibling order.
        var closestDepth = pool.Min(c => c.Depth);
        var closest = pool.Where(c => c.Depth == closestDepth).ToList();
        if (closest.Count > 1)
            throw new InvalidOperationException($"{closest.Count} equally scoped forms match: " +
                string.Join("; ", closest.Select(c => c.Match.Path)));
        return closest[0].Match;
    }

    public static void EnterFrames(IWebDriver driver, IReadOnlyList<IWebElement> frames)
    {
        driver.SwitchTo().DefaultContent();
        foreach (var frame in frames) driver.SwitchTo().Frame(frame);
    }

    private static void VisitRoot(IWebDriver driver, IWebElement? shadowHost, string path,
        IReadOnlyList<IWebElement> frames, HashSet<string> ancestorUrls, int depth,
        string heading, bool allowLabelFallback, ScanState state)
    {
        if (depth > MaxDepth || state.Contexts >= MaxContexts)
        {
            state.Skipped++;
            return;
        }
        state.Contexts++;
        ISearchContext root = shadowHost is null ? driver : shadowHost.GetShadowRoot();

        foreach (var form in SemanticForm.VisibleForms(root))
        {
            var headingMatch = SemanticForm.HasHeading(form, heading);
            var labelMatch = !headingMatch && allowLabelFallback && SemanticForm.HasAllFieldLabels(form);
            if (!headingMatch && !labelMatch) continue;
            var reason = headingMatch ? $"heading=\"{heading}\"" : "labels=First Name, Gender, State";
            state.Candidates.Add(new Candidate(
                new Match(form, $"{path} > form[{reason}]", frames.ToArray()), depth, headingMatch, labelMatch));
        }

        if (depth == MaxDepth) return;
        state.SlotAssignments.AddRange(AssignedSlots(driver, shadowHost)
            .Select(assignment => $"{path} > slot[{assignment}]"));

        var hosts = QueryElements(driver, shadowHost, "shadow");
        for (var i = 0; i < hosts.Count; i++)
        {
            var host = hosts[i];
            try
            {
                VisitRoot(driver, host, $"{path} > shadow:{host.TagName}[{i + 1}]", frames,
                    ancestorUrls, depth + 1, heading, allowLabelFallback, state);
            }
            catch (WebDriverException ex)
            {
                state.Skipped++;
                state.Errors.Add($"{path} > shadow: {ex.Message.Split('\n')[0]}");
            }
        }

        var childFrames = QueryElements(driver, shadowHost, "iframe");
        for (var i = 0; i < childFrames.Count; i++)
        {
            var frame = childFrames[i];
            var source = frame.GetDomAttribute("src") ?? "srcdoc";
            var framePath = $"{path} > iframe[{i + 1}, src=\"{source}\"]";
            var entered = false;
            try
            {
                driver.SwitchTo().Frame(frame);
                entered = true;
                var url = CurrentUrl(driver);
                if (IsRepeatableUrl(url) && ancestorUrls.Contains(url))
                {
                    state.RepeatedUrls++;
                    continue;
                }
                var nextUrls = new HashSet<string>(ancestorUrls, StringComparer.Ordinal) { url };
                var nextFrames = frames.Append(frame).ToArray();
                VisitRoot(driver, null, framePath, nextFrames, nextUrls,
                    depth + 1, heading, allowLabelFallback, state);
            }
            catch (WebDriverException ex)
            {
                state.Skipped++;
                state.Errors.Add($"{framePath}: {ex.Message.Split('\n')[0]}");
            }
            finally
            {
                if (entered) driver.SwitchTo().ParentFrame();
            }
        }
    }

    private static string CurrentUrl(IWebDriver driver) =>
        ((IJavaScriptExecutor)driver).ExecuteScript("return location.href") as string
        ?? throw new InvalidOperationException("Could not read the current browsing-context URL");

    private static bool IsRepeatableUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file";

    private static IEnumerable<string> AssignedSlots(IWebDriver driver, IWebElement? shadowHost)
    {
        var result = ((IJavaScriptExecutor)driver).ExecuteScript("""
            const root = arguments[0] ? arguments[0].shadowRoot : document;
            return [...root.querySelectorAll('slot')]
                .flatMap(slot => slot.assignedElements()
                    .map(element => `${slot.name || '(default)'}:${element.localName}`));
            """, shadowHost);
        if (result is not IEnumerable assignments)
            throw new InvalidOperationException("Browser did not return slot assignments");
        return assignments.Cast<string>().ToList();
    }

    private static List<IWebElement> QueryElements(IWebDriver driver, IWebElement? shadowHost, string kind)
    {
        var result = ((IJavaScriptExecutor)driver).ExecuteScript("""
            const root = arguments[0] ? arguments[0].shadowRoot : document;
            if (arguments[1] === 'shadow')
                return [...root.querySelectorAll('*')].filter(element => element.shadowRoot);
            return [...root.querySelectorAll('iframe')];
            """, shadowHost, kind);
        if (result is not IEnumerable elements)
            throw new InvalidOperationException("Browser did not return a context element list");
        return elements.Cast<IWebElement>().ToList();
    }
}
