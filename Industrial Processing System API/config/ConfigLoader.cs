using System.Xml.Linq;
using Industrial_Processing_System_API.models;

namespace Industrial_Processing_System_API.config;

public static class ConfigLoader
{
    public static SystemConfig Load(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root!;

        var config = new SystemConfig
        {
            WorkerCount = int.Parse(root.Element("WorkerCount")!.Value),
            MaxQueueSize = int.Parse(root.Element("MaxQueueSize")!.Value),
            
            Jobs = root.Element("Jobs")!
                .Elements("Job")
                .Select(e => new Job
                {
                    Id = Guid.NewGuid(),
                    Type = Enum.Parse<JobType>(e.Attribute("Type")!.Value),
                    Payload = e.Attribute("Payload")!.Value,
                    Priority = int.Parse(e.Attribute("Priority")!.Value)
                })
                .ToList()
        };

        return config;
    }
}