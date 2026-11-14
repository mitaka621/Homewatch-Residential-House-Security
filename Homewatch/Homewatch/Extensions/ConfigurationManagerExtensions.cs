using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace Homewatch.Extensions
{
    public static class ConfigurationManagerExtensions
    {
        private const string _hierarchySeparator = "__";

        public static ConfigurationManager AddEnvironmentFile(this ConfigurationManager configuration, string filePath)
        {
            if (!File.Exists(filePath))
            {
                return configuration;
            }

            var environmentFileSource = new MemoryConfigurationSource
            {
                InitialData = ReadEnvironmentFile(filePath)
            };

            var environmentVariablesSourceIndex = FindEnvironmentVariablesSourceIndex(configuration);

            if (environmentVariablesSourceIndex < 0)
            {
                configuration.Sources.Add(environmentFileSource);
            }
            else
            {
                configuration.Sources.Insert(environmentVariablesSourceIndex, environmentFileSource);
            }

            return configuration;
        }

        private static Dictionary<string, string?> ReadEnvironmentFile(string filePath)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var fileLine in File.ReadAllLines(filePath))
            {
                var line = fileLine.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');

                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim().Replace(_hierarchySeparator, ConfigurationPath.KeyDelimiter);
                var value = line[(separatorIndex + 1)..].Trim();

                values[key] = RemoveSurroundingQuotes(value);
            }

            return values;
        }

        private static string RemoveSurroundingQuotes(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                return value[1..^1];
            }

            return value;
        }

        private static int FindEnvironmentVariablesSourceIndex(ConfigurationManager configuration)
        {
            for (var index = 0; index < configuration.Sources.Count; index++)
            {
                if (configuration.Sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
