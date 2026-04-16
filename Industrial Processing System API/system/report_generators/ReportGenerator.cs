using System.Xml.Linq;
using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.report_generators;

public class ReportGenerator : IReportGenerator
{
    private readonly string _reportFolder;
    private int _reportIndex = 0;
    private const int MaxReports = 10;

    public ReportGenerator(string reportFolder = "reports")
    {
        _reportFolder = reportFolder;
        Directory.CreateDirectory(reportFolder);
    }

    public void GenerateReport(IEnumerable<(Job job, int result, bool failed, TimeSpan duration)> completedJobs)
    {
        var jobList = completedJobs.ToList();

        // LINQ statistics
        var completedByType = jobList
            .Where(j => !j.failed)
            .GroupBy(j => j.job.Type)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count(),
                AvgDuration = g.Average(j => j.duration.TotalMilliseconds)
            });

        var failedByType = jobList
            .Where(j => j.failed)
            .GroupBy(j => j.job.Type)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count()
            });

        // XML
        var doc = new XDocument(
            new XElement("Report",
                new XAttribute("GeneratedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),

                new XElement("CompletedJobs",
                    completedByType.Select(c =>
                        new XElement("Type",
                            new XAttribute("Name", c.Type),
                            new XAttribute("Count", c.Count),
                            new XAttribute("AvgDurationMs", Math.Round(c.AvgDuration, 2))
                        )
                    )
                ),

                new XElement("FailedJobs",
                    failedByType.Select(f =>
                        new XElement("Type",
                            new XAttribute("Name", f.Type),
                            new XAttribute("Count", f.Count)
                        )
                    )
                )
            )
        );

        // Rotation
        string fileName = Path.Combine(_reportFolder, $"report_{_reportIndex % MaxReports}.xml");
        doc.Save(fileName);
        _reportIndex++;
    }
}