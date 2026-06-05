using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Virinco.WATS.Interface;
using Virinco.WATS.Interface.Models;

namespace Virinco.WATS.Converter.Teradyne
{
    public class TeradyneNano51eConverter : IReportConverter_v2
    {
        private Dictionary<string, string> arguments;

        public Dictionary<string, string> ConverterParameters => arguments;

        public TeradyneNano51eConverter()
        {
            arguments =
               new Dictionary<string, string>() {
                {"operationTypeCode","30" },
                {"operator","sysoper" },
                {"sequenceVersion","" },
                {"sequenceFileName","" },
                {"revision","" },
                {"groupSteps","true"}
            };
        }

        public TeradyneNano51eConverter(Dictionary<string, string> args)
        {
            arguments = args;
        }

        public void CleanUp()
        {
        }

        public Report ImportReport(TDM api, Stream file)
        {

            string fileName = Path.GetFileNameWithoutExtension((file as FileStream)?.Name);

            Regex regex = new Regex(@"^A%(.*)%(.*)%(.*)_(.*)_(.*)$");
            Match match = regex.Match(fileName);

            if (match.Success)
            {
                Dictionary<string, SequenceCall> sequenceCallsDict = new Dictionary<string, SequenceCall>();
                string partNumber = match.Groups[1].Value;
                string smtDate = match.Groups[2].Value;
                string serialNumber = match.Groups[3].Value;
                UUTStatusType reportStatus = GetUUTStatusType(match.Groups[4].Value);
                DateTime testDate = parseStringDateTime(match.Groups[5].Value);

                UUTReport uut = api.CreateUUTReport(
                    ConverterParameters["operator"],
                    partNumber,
                    ConverterParameters["revision"],
                    serialNumber,
                    ConverterParameters["operationTypeCode"],
                    ConverterParameters["sequenceFileName"],
                    ConverterParameters["sequenceVersion"]
                    );


                using (var reader = new StreamReader(file))
                {
                    string line;

                    reader.ReadLine();

                    SequenceCall currentSequence = uut.GetRootSequenceCall();

                    while ((line = reader.ReadLine()) != null)
                    {
                        var parts = line.Split(',');

                        (string testName, string group) = SplitPartIntoTestNameAndGroup(parts[0]);
                        (double value, string unit) = SplitActualValueIntoValueAndUnit(parts[1]);
                        double lowLimit = parseStringToDouble(parts[2]);
                        double highLimit = parseStringToDouble(parts[3]);
                        StepStatusType stepStatus = GetStepStatusType(parts[4]);

                        if (ConverterParameters["groupSteps"].ToUpper() != "TRUE")
                        {
                            NumericLimitStep currentStep = currentSequence.AddNumericLimitStep(testName);

                            currentStep.AddTest(value, CompOperatorType.GELE, lowLimit, highLimit, unit, stepStatus);
                        } else
                        {
                           
                            if (!sequenceCallsDict.ContainsKey(group))
                            {
                                sequenceCallsDict.Add(group, uut.GetRootSequenceCall().AddSequenceCall(group));
                            }

                            NumericLimitStep currentStep = sequenceCallsDict[group].AddNumericLimitStep(testName);

                            currentStep.AddTest(value, CompOperatorType.GELE, lowLimit, highLimit, unit, stepStatus);

                        }
                    }

                   
                    
                }

                uut.Status = reportStatus;
                uut.StartDateTime = testDate;
                uut.AddMiscUUTInfo("SMT Production Date", smtDate);
                api.Submit(uut);

            } else
            {
                throw new FormatException("Could parse the filename: " + fileName);
            }

            return null;
        }

        private UUTStatusType GetUUTStatusType(string status)
        {
            if (status == "OK")
            {
                return UUTStatusType.Passed;
            }
            else
            {
                return UUTStatusType.Failed;
            }
        }

        private StepStatusType GetStepStatusType(string status)
        {
            if (status == "PASS")
            {
                return StepStatusType.Passed;
            }
            else
            {
                return StepStatusType.Failed;
            }
        }

        private (double, string) SplitActualValueIntoValueAndUnit(string actualValue)
        {
            int index = actualValue.Length - 1;
         
            while (index > 0 && char.IsLetter(actualValue[index]))
            {
                index--;
            }

            double value = parseStringToDouble(actualValue.Substring(0, index + 1));
            string unit = actualValue.Substring(index + 1);
            return (value, unit);
        }

        private (string, string) SplitPartIntoTestNameAndGroup(string part)
        {
            Regex regex = new Regex(@"^(.+)\[(.+)\]$");
            Match match = regex.Match(part);

            if (match.Success)
            {
                string testName = match.Groups[1].Value;
                string group = match.Groups[2].Value;

                return (testName, group);
            }
            else
            {
                throw new FormatException("Could not split part into testName and group: " + part);
            }
        }


        private double parseStringToDouble(string doubleString)
        {
            // Remove unit
            doubleString = new string(doubleString.Where(c => char.IsDigit(c) || c == ',' || c == '.' || c == '-').ToArray());


            // Replace ',' with '.' 
            doubleString = doubleString.Replace(',', '.');

            double parsed;
            if (Double.TryParse(doubleString, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            else
            {
                throw new FormatException("Could not parse string to double: " + doubleString);
         
            }
        }

        private DateTime parseStringDateTime(string dateTime)
        {
            string format = "yyyyMMddHHmmss";

            DateTime result;
            if (DateTime.TryParseExact(dateTime, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }
            else
            {
                throw new FormatException($"String {dateTime} does not match the datetime format: {format}");
            }
        }
    }
}
