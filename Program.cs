using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace splitprtimings {
	class MainClass {
		static DateTime firstDate;
		static DateTime previosDate;
		static List<Tuple<DateTime, string>> Lines;
		static Dictionary<string, string> Mapped = new Dictionary<string, string> ();

		static void FindLine (string contents, string title = null, bool regexp = false, string action = null)
		{
			var lines = Lines.Where ((v) => {
				if (regexp)
					return System.Text.RegularExpressions.Regex.IsMatch (v.Item2, contents);
				else
					return v.Item2 == contents;
			}).ToList ();
			if (!lines.Any ()) {
				Console.WriteLine ($"Could not find '{contents}'.");
				return;
			}
			if (lines.Count () > 1)
				Console.WriteLine ($"Found {lines.Count} lines matching '{contents}'.");
			var line = lines.First ();
			var diff = line.Item1 - previosDate;
			var diffSinceStart = line.Item1 - firstDate;
			if (title == null)
				title = $"Found '{contents}'";
			Console.WriteLine ($"{title} {diff.TotalSeconds}s = {diff.ToString ()} later, {diffSinceStart.TotalSeconds}s = {diffSinceStart} after starting.");
			if (action != null) {
				Console.WriteLine ($"    {action}: {diff}");
				Mapped.Add (action, diff.ToString ());
			}
			previosDate = line.Item1;
		}
		public static int Main (string [] args)
		{
			var actions = new string [] {
				"Date",
				"Url",
				"PR",
				"Bot",
				"Build",
				"API diff",
				"API/generator comparison",
				"Run tests",
				"Publish HTML report",
				"Upload HTML report",
				"Total time",
};
			var sb = new StringBuilder ();
			sb.Append ("| ");
			for (var i = 0; i < actions.Length; i++)
				sb.Append ($" {actions [i]} | ");
			sb.AppendLine ();
			sb.Append ("| ");
			for (var i = 0; i < actions.Length; i++)
				sb.Append ($" - | ");
			sb.AppendLine ();

			foreach (var arg in args) {
				if (!int.TryParse (arg, out var jobNumber)) {
					Console.WriteLine ("Arguments must be job numbers (ints).");
					return 1;
				}

				Mapped.Clear ();
				firstDate = DateTime.MinValue;
				previosDate = DateTime.MinValue;
				Process (jobNumber);
				sb.Append ("| ");
				for (var i = 0; i < actions.Length; i++)
					sb.Append ($" {Mapped [actions [i]]} |");
				sb.AppendLine ();

			}

			Console.WriteLine (sb);

			return 0;
		}

		static void Process (int jobNumber)
		{
			Console.WriteLine ($"\nProcessing {jobNumber}\n");
			var fn = $"/tmp/jenkins-job-{jobNumber}.txt";
			var url = $"https://jenkins.mono-project.com/job/xamarin-macios-pr-builder/{jobNumber}/consoleFull";
			if (!File.Exists (fn) || new FileInfo (fn).Length <= 0) {
				Console.WriteLine ($"Downloading {url}...");
				var wc = new WebClient ();
				wc.DownloadFile (url, fn);
				Console.WriteLine ($"Downloaded {url}.");
			}
			Mapped.Add ("Url", $"[#{jobNumber}]({url})");


			var lines = (IEnumerable<string>) File.ReadAllLines (fn);
			lines = lines.Where ((v) => v.StartsWith ("<span class=\"timestamp\">", StringComparison.Ordinal));

			var splitLines = lines.Select ((v) => {
				var timestamp = v.Substring (27, 8);
				var line = v.Substring (47);
				var dt = DateTime.ParseExact (timestamp, "HH:mm:ss", CultureInfo.InvariantCulture);
				if (firstDate == DateTime.MinValue) {
					firstDate = dt;
				} else if (dt < firstDate) {
					// Next day
					dt = dt.AddDays (1);
				}
				return new Tuple<DateTime, string> (dt, line);
			});

			Lines = splitLines.ToList ();

			var dateStarted = Lines.First ((v) => v.Item2.StartsWith ("Build started ", StringComparison.Ordinal)).Item2;
			dateStarted = dateStarted.Substring ("Build started ".Length);
			dateStarted = dateStarted.Substring (0, dateStarted.IndexOf (' '));
			Mapped.Add ("Date", DateTime.ParseExact (dateStarted, "M/d/yyyy", CultureInfo.InvariantCulture).ToString ("yyyy/MM/dd"));

			var bot = Lines.First ((v) => v.Item2.StartsWith ("Building remotely on", StringComparison.Ordinal)).Item2;
			bot = bot.Substring (bot.IndexOf ('>') + 1);
			bot = bot.Substring (0, bot.IndexOf ('<'));
			Mapped.Add ("Bot", bot);

			var pr = Lines.First ((v) => v.Item2.StartsWith (" &gt; git rev-parse refs/remotes/origin/pr/", StringComparison.Ordinal)).Item2;
			pr = pr.Substring (" &gt; git rev-parse refs/remotes/origin/pr/".Length);
			pr = pr.Substring (0, pr.IndexOf ('/'));
			Mapped.Add ("PR", $"[#{pr}](https://github.com/xamarin/xamarin-macios/pull/{pr})");

			previosDate = firstDate;
			Console.WriteLine ($"Job started at {firstDate.ToString ("HH:mm:ss")}");
			FindLine ("+ ./jenkins/provision-deps.sh");
			FindLine ("+ ./jenkins/build.sh", action: "Provisioning");
			FindLine ("LN       Current", "Build started");
			FindLine ("Validated file permissions for Xamarin.iOS.", "Build completed", action: "Build");
			FindLine ("+ ./jenkins/build-api-diff.sh", "API diff starting");
			FindLine ("+ ./jenkins/compare.sh", "API diff completed, starting API/generator comparison", action: "API diff");
			FindLine ("+ ./jenkins/run-tests.sh", "API/generator comparison completed, starting test run", action: "API/generator comparison");
			FindLine ("+ touch /Users/builder/jenkins/workspace/xamarin-macios-pr-builder/build-completed.stamp", "Test run completed, starting post-job tasks", action: "Run tests");
			FindLine ("MicrosoftAzureStorage - Uploading files to Microsoft Azure", "Published HTML report", action: "Publish HTML report");
			FindLine ("MicrosoftAzureStorage - Uploaded/archived file count.*", "Uploaded HTML report", true, action: "Upload HTML report");
			var lastLine = Lines.Last ();
			FindLine (lastLine.Item2, "Job completed", action: "Post-job actions");
			previosDate = firstDate;
			FindLine (lastLine.Item2, "Job completed", action: "Total time");
		}
	}
}
