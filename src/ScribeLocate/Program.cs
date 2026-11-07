using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;

Settings settings;
try
{
    settings = Settings.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"ARGUMENT ERROR: {ex.Message}");
    Environment.ExitCode = 2;
    return;
}
var options = new FirefoxOptions { AcceptInsecureCertificates = true, PageLoadStrategy = PageLoadStrategy.Eager };
if (!settings.Headed) options.AddArgument("-headless");
FirefoxDriver driver;
try
{
    driver = new FirefoxDriver(options);
}
catch (WebDriverException ex)
{
    Console.Error.WriteLine($"BROWSER ERROR: Firefox could not start: {ex.Message}");
    if (settings.Headed)
        Console.Error.WriteLine("Headed mode needs an active graphical desktop. Run without --headed for headless testing.");
    Environment.ExitCode = 1;
    return;
}
using var browser = driver;

try
{
    driver.Navigate().GoToUrl(settings.Url);
    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20)) { PollingInterval = TimeSpan.FromMilliseconds(200) };
    wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
    ContextExplorer.Match match;
    try
    {
        match = wait.Until(d => ContextExplorer.Find(d, settings.FormHeading));
    }
    catch (WebDriverTimeoutException)
    {
        var description = settings.FormHeading is null
            ? "visible form headed 'Form' or containing all three field labels"
            : $"visible form headed '{settings.FormHeading}'";
        throw new InvalidOperationException($"No {description} appeared within 20 seconds ({ContextExplorer.LastScanSummary})");
    }
    Console.WriteLine($"TARGET {match.Path}");
    Console.WriteLine($"DISCOVERY {ContextExplorer.LastScanSummary}");
    IWebElement CurrentForm()
    {
        var current = ContextExplorer.Find(driver, settings.FormHeading)
            ?? throw new FieldNotReadyException("Target form has not appeared yet");
        ContextExplorer.EnterFrames(driver, current.FrameChain);
        return current.Element;
    }

    var results = new List<bool>();
    results.Add(RunCheck("First Name", () => ExerciseWithRetry(
        "First Name",
        () => SemanticForm.FindControl(CurrentForm(), "First Name", "input", "text"),
        firstName => { firstName.Clear(); firstName.SendKeys(settings.FirstName); },
        firstName => firstName.GetDomProperty("value") == settings.FirstName)));

    PauseBetweenFields(settings.FieldDelayMs);
    results.Add(RunCheck("Gender", () => ExerciseWithRetry(
        "Gender",
        () => SemanticForm.FindRadio(CurrentForm(), settings.Gender),
        gender => gender.Click(),
        gender => gender.Selected)));

    PauseBetweenFields(settings.FieldDelayMs);
    results.Add(RunCheck("State", () => ExerciseWithRetry(
        "State",
        () => SemanticForm.FindControl(CurrentForm(), "State", "select"),
        state =>
        {
            var dropdown = new SelectElement(state);
            var options = dropdown.Options.Where(option => SemanticForm.Same(option.Text, settings.State)).ToList();
            if (options.Count == 0) throw new FieldNotReadyException($"State option '{settings.State}' has not appeared yet");
            if (options.Count != 1) throw new InvalidOperationException($"State has {options.Count} options named '{settings.State}'");
            var chosen = options[0];
            if (!chosen.Enabled || string.IsNullOrWhiteSpace(chosen.GetDomProperty("value")) ||
                IsPlaceholder(chosen.Text))
                throw new InvalidOperationException($"State option '{settings.State}' is a placeholder or disabled");
            chosen.Click();
        },
        state => SemanticForm.Same(new SelectElement(state).SelectedOption.Text, settings.State))));

    if (results.Any(passed => !passed)) Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"TARGET ERROR {ex.Message}");
    foreach (var field in new[] { "First Name", "Gender", "State" })
        Console.Error.WriteLine($"FAIL {field}: target form unavailable");
    Environment.ExitCode = 1;
}

if (settings.KeepOpenSeconds > 0)
{
    Console.WriteLine($"KEEP OPEN Browser remains open for {settings.KeepOpenSeconds} second(s)");
    Thread.Sleep(TimeSpan.FromSeconds(settings.KeepOpenSeconds));
}

static void PauseBetweenFields(int delayMs)
{
    if (delayMs == 0) return;
    Console.WriteLine($"WAIT {delayMs} ms before next field");
    Thread.Sleep(delayMs);
}

static bool RunCheck(string field, Action check)
{
    try
    {
        check();
        Console.WriteLine($"PASS {field}");
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {field}: {ex.Message}");
        return false;
    }
}

static void ExerciseWithRetry(string field, Func<IWebElement> resolve, Action<IWebElement> act,
    Func<IWebElement, bool> verify)
{
    var clock = Stopwatch.StartNew();
    var timeout = TimeSpan.FromSeconds(12);
    string lastIssue = "control did not become usable";
    while (true)
    {
        try
        {
            var control = resolve();
            act(control);
            if (verify(resolve())) return;
            lastIssue = "result did not persist";
        }
        catch (Exception ex) when (ex is FieldNotReadyException or StaleElementReferenceException
            or NoSuchElementException or NoSuchFrameException or ElementNotInteractableException
            or ElementClickInterceptedException)
        {
            lastIssue = ex.Message;
        }
        if (clock.Elapsed >= timeout)
            throw new InvalidOperationException($"{field} did not pass within {timeout.TotalSeconds:0} seconds: {lastIssue}");
        Thread.Sleep(200);
    }
}

static bool IsPlaceholder(string text) =>
    new[] { "Choose", "Select", "Select State", "Please select" }
        .Any(placeholder => SemanticForm.Same(text, placeholder));

internal sealed record Settings(string Url, string FirstName, string Gender, string State,
    string? FormHeading, bool Headed, int FieldDelayMs, int KeepOpenSeconds)
{
    public static Settings Parse(string[] args)
    {
        string? url = null;
        var firstName = "Ada";
        var gender = "Female";
        var state = "India";
        string? formHeading = null;
        var headed = false;
        var fieldDelayMs = 0;
        var keepOpenSeconds = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--headed") { headed = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
            var key = args[i];
            var value = args[++i];
            switch (key)
            {
                case "--url": url = value; break;
                case "--first-name": firstName = value; break;
                case "--gender": gender = value; break;
                case "--state": state = value; break;
                case "--form-heading": formHeading = value; break;
                case "--field-delay-ms":
                    if (!int.TryParse(value, out fieldDelayMs) || fieldDelayMs < 0)
                        throw new ArgumentException("--field-delay-ms must be a nonnegative integer");
                    break;
                case "--keep-open-seconds":
                    if (!int.TryParse(value, out keepOpenSeconds) || keepOpenSeconds is < 0 or > 86400)
                        throw new ArgumentException("--keep-open-seconds must be an integer from 0 to 86400");
                    break;
                default: throw new ArgumentException($"Unknown argument {key}");
            }
        }
        url ??= "https://app.cloudqa.io/home/AutomationPracticeForm";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "file"))
            throw new ArgumentException("--url must be an absolute http(s) or file URL");
        if (formHeading is not null && string.IsNullOrWhiteSpace(formHeading))
            throw new ArgumentException("--form-heading must contain visible heading text");
        if (string.IsNullOrWhiteSpace(gender) || string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("--gender and --state must contain an option name");
        return new Settings(url, firstName, gender, state, formHeading, headed, fieldDelayMs, keepOpenSeconds);
    }
}
