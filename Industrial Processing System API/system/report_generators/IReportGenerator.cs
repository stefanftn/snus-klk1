using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.system.report_generators;

public interface IReportGenerator
{
    void GenerateReport(IEnumerable<(Job job, int result, bool failed, TimeSpan duration)> completedJobs);
}