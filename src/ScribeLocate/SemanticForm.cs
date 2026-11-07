using System.Collections;
using OpenQA.Selenium;

internal static class SemanticForm
{
    public static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool HasHeading(IWebElement form, string heading) =>
        form.FindElements(By.CssSelector("h1,h2,h3,h4,h5,h6"))
            .Any(h => h.Displayed && Same(h.Text, heading));

    public static bool HasAllFieldLabels(IWebElement form) =>
        new[] { "First Name", "Gender", "State" }.All(name =>
            form.FindElements(By.CssSelector("label")).Any(label =>
                label.Displayed && Same(LabelText(label), name)));

    private static string? LabelText(IWebElement label)
    {
        var driver = (IJavaScriptExecutor)((IWrapsDriver)label).WrappedDriver;
        return driver.ExecuteScript("""
            const copy = arguments[0].cloneNode(true);
            copy.querySelectorAll('input,select,textarea,button').forEach(node => node.remove());
            return copy.textContent;
            """, label) as string;
    }

    public static IReadOnlyList<IWebElement> VisibleForms(ISearchContext context) =>
        context.FindElements(By.CssSelector("form")).Where(form => form.Displayed).ToList();

    public static IWebElement FindControl(IWebElement form, string label, string tag, string? inputType = null) =>
        Resolve(form, label, tag, inputType, null);

    public static IWebElement FindRadio(IWebElement form, string option) =>
        Resolve(form, "Gender", "input", "radio", option);

    private static IWebElement Resolve(IWebElement form, string label, string tag, string? type, string? option)
    {
        var driver = (IJavaScriptExecutor)((IWrapsDriver)form).WrappedDriver;
        var result = driver.ExecuteScript(ResolverScript, form, label, tag, type, option);
        if (result is not IEnumerable items)
            throw new InvalidOperationException("Field resolver returned an unexpected result");
        var values = items.Cast<object?>().ToArray();
        if (values.Length != 3) throw new InvalidOperationException("Field resolver returned an unexpected result");
        if (values[0] is IWebElement element) return element;
        var message = values[2]?.ToString() ?? $"Could not resolve {label}";
        if (values[1]?.ToString() == "missing") throw new FieldNotReadyException(message);
        throw new InvalidOperationException(message);
    }

    private const string ResolverScript = """
        const [form, wantedLabel, tag, type, wantedOption] = arguments;
        const norm = value => String(value ?? '').replace(/\s+/g, ' ').trim().toLowerCase();
        const visible = node => node.getClientRects().length > 0 &&
            getComputedStyle(node).visibility !== 'hidden' && getComputedStyle(node).display !== 'none';
        const parent = new Map(), nodes = [], seen = new Set();
        function visit(node, owner) {
            if (!node || seen.has(node)) return;
            seen.add(node);
            parent.set(node, owner);
            nodes.push(node);
            let children;
            if (node.localName === 'slot') {
                children = node.assignedElements({flatten: true});
                if (!children.length) children = node.children;
            } else if (node.shadowRoot) {
                children = node.shadowRoot.children;
            } else {
                children = node.children;
            }
            for (const child of children) visit(child, node);
        }
        visit(form, null);
        function below(node, ancestor) {
            for (let current = node; current; current = parent.get(current))
                if (current === ancestor) return true;
            return false;
        }
        function labelText(label) {
            const copy = label.cloneNode(true);
            copy.querySelectorAll('input,select,textarea,button').forEach(node => node.remove());
            return norm(copy.textContent);
        }
        const labels = nodes.filter(node => node.localName === 'label' && visible(node));
        const matches = nodes.filter(node => node.localName === tag &&
            (!type || norm(node.getAttribute('type') || 'text') === norm(type)) &&
            visible(node) && !node.disabled &&
            !(node.localName === 'input' && type === 'text' && node.readOnly));
        const targetLabels = labels.filter(node => labelText(node) === norm(wantedLabel));
        const evidence = new Map();
        function credit(node, score, reason) {
            const old = evidence.get(node);
            if (!old || score > old.score) evidence.set(node, {score, reason});
        }
        for (const control of matches) {
            if (norm(control.getAttribute('aria-label')) === norm(wantedLabel))
                credit(control, 4, 'aria-label');
            const ids = (control.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
            if (ids.length) {
                const text = ids.map(id => nodes.find(node => node.id === id)?.textContent || '').join(' ');
                if (norm(text) === norm(wantedLabel)) credit(control, 4, 'aria-labelledby');
            }
        }
        for (const group of nodes) {
            const groupName = group.getAttribute('aria-label') ||
                (group.localName === 'fieldset'
                    ? [...group.children].find(child => child.localName === 'legend')?.textContent : null);
            if (norm(groupName) !== norm(wantedLabel)) continue;
            if (group.localName !== 'fieldset' && group.getAttribute('role') !== 'radiogroup') continue;
            for (const control of matches.filter(control => below(control, group)))
                credit(control, 3, `${group.localName} group name`);
        }
        for (const label of targetLabels) {
            const forId = label.getAttribute('for');
            for (const control of matches) {
                if (forId && control.id === forId) credit(control, 4, 'label for');
                if (below(control, label)) credit(control, 4, 'wrapping label');
            }
            for (let group = label; group && group !== form; group = parent.get(group)) {
                const controls = matches.filter(control => below(control, group));
                if (!controls.length) continue;
                const otherLabels = labels.filter(other => below(other, group) &&
                    labelText(other) !== norm(wantedLabel));
                if (!otherLabels.length)
                    for (const control of controls) credit(control, 2, 'nearest labeled group');
                break;
            }
        }
        let candidates = [...evidence].map(([node, data]) => ({node, ...data}));
        if (wantedOption !== null) {
            candidates = candidates.filter(({node}) => {
                const next = node.nextElementSibling;
                const associated = labels.filter(label => label.control === node)
                    .map(label => labelText(label));
                return [node.value, node.getAttribute('aria-label'), next?.textContent,
                    ...associated].some(value => norm(value) === norm(wantedOption));
            });
        }
        const bestScore = Math.max(0, ...candidates.map(item => item.score));
        const best = candidates.filter(item => item.score === bestScore);
        if (best.length === 1) return [best[0].node, null, null];
        function describe(node) {
            const chain = [];
            for (let current = node; current && current !== form; current = parent.get(current)) {
                const owner = parent.get(current);
                const siblings = nodes.filter(item => parent.get(item) === owner &&
                    item.localName === current.localName);
                const position = siblings.length > 1 ? `[${siblings.indexOf(current) + 1}]` : '';
                chain.push(current.localName === 'slot'
                    ? `slot(${current.name || 'default'})${position}`
                    : `${current.localName}${position}${current.shadowRoot ? '(shadow host)' : ''}`);
            }
            const attributes = ['id', 'name', 'slot', 'value'].map(key =>
                node.getAttribute(key) ? `${key}=${node.getAttribute(key)}` : '').filter(Boolean);
            return `${chain.reverse().join(' > ')}${attributes.length ? ` {${attributes.join(', ')}}` : ''}`;
        }
        const detail = best.length
            ? best.map(item => `${describe(item.node)} (${item.reason})`).join('; ')
            : `${targetLabels.length} matching labels, ${matches.length} usable ${tag} candidates`;
        return [null, best.length ? 'ambiguous' : 'missing',
            `${wantedLabel}${wantedOption === null ? '' : ` / ${wantedOption}`}: ` +
            (best.length ? `ambiguous ${best.length} candidates: ` : 'no associated control: ') + detail];
        """;
}

internal sealed class FieldNotReadyException(string message) : Exception(message);
