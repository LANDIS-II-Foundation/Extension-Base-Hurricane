//  Authors:  Robert M. Scheller, James B. Domingo

using Landis.SpatialModeling;
using Landis.Core;
using Landis.Library.Metadata;
using Troschuetz.Random.Distributions.Continuous;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

namespace Landis.Extension.BaseHurricane
{
    ///<summary>
    /// A disturbance plug-in that simulates hurricane wind disturbance.
    /// </summary>

    public class PlugIn
        : ExtensionMain
    {
        public static readonly ExtensionType ExtType = new ExtensionType("disturbance:hurricane");
        public static MetadataTable<EventsLog> eventLog;
        public static MetadataTable<SummaryLog> summaryLog;
        public static readonly string ExtensionName = "Base Hurricane";
        //public static ContinuousUniformDistribution hurricaneGeneratorStandard;
        public static Troschuetz.Random.Distributions.Continuous.LognormalDistribution HurricaneGeneratorLogNormal;
        public static Troschuetz.Random.Generators.MT19937Generator HurricaneGeneratorStandard;

        private string mapNameTemplate;
        private IInputParameters parameters;
        //private WindSpeedGenerator windSpeedGenerator = null;
        private static ICore modelCore;
        private int summaryTotalSites = 0;
        private int summaryEventCount = 0;
        private ContinentalGrid ContinentalGrid = null;


        //---------------------------------------------------------------------

        public PlugIn()
            : base("Base Hurricane", ExtType)
        {
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }
        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile, ICore mCore)
        {
            modelCore = mCore;
            InputParameterParser parser = new InputParameterParser();
            this.parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
        }
        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes the plug-in with a data file.
        /// </summary>
        /// <param name="dataFile">
        /// Path to the file with initialization data.
        /// </param>
        /// <param name="startTime">
        /// Initial timestep (year): the timestep that will be passed to the
        /// first call to the component's Run method.
        /// </param>
        public override void Initialize()
        {

            HurricaneEvent.WindMortalityTable = new HurricaneWindMortalityTable(parameters.WindSpeedMortalityProbabilities);
            if (parameters.InputUnitsEnglish)
            {
                parameters.LowBoundLandfallWindSpeed *= 1.60934;
                parameters.ModeLandfallWindSpeed *= 1.60934;
                parameters.HighBoundLandfallWindspeed *= 1.60934;
                parameters.CenterPointDistanceInland *= 1.60934;
                //HurricaneEvent.WindMortalityTable.ChangeSpeedsFromEnglishToMetric();

            }
            HurricaneEvent.WindSpeedGenerator = new WindSpeedGenerator(this.parameters.LowBoundLandfallWindSpeed,
                this.parameters.ModeLandfallWindSpeed, this.parameters.HighBoundLandfallWindspeed);
            //parameters.AdjustValuesFromEnglishToMetric();

            List<string> colnames = new List<string>();
            foreach(IEcoregion ecoregion in modelCore.Ecoregions)
            {
                colnames.Add(ecoregion.Name);
            }
            ExtensionMetadata.ColumnNames = colnames;

            MetadataHandler.InitializeMetadata(parameters.Timestep, parameters.MapNamesTemplate);

            Timestep = parameters.Timestep;
            mapNameTemplate = parameters.MapNamesTemplate;

            SiteVars.Initialize();
            this.ContinentalGrid = new ContinentalGrid(
                this.parameters.CenterPointLatitude, 
                PlugIn.ModelCore.CellLength,
                PlugIn.ModelCore.Landscape.Columns,
                PlugIn.ModelCore.Landscape.Rows,
                this.parameters.CenterPointDistanceInland
                );

            if (parameters.HurricaneRandomNumberSeed > 0)
            {
                HurricaneEvent.HurricaneRandomNumber = true;
                HurricaneGeneratorStandard = new Troschuetz.Random.Generators.MT19937Generator((uint)parameters.HurricaneRandomNumberSeed);
                HurricaneGeneratorLogNormal = new Troschuetz.Random.Distributions.Continuous.LognormalDistribution((uint)parameters.HurricaneRandomNumberSeed);
            }

            //double testDouble = hurricaneGenerator.NextDouble();
        }

        //---------------------------------------------------------------------

        ///<summary>
        /// Run the plug-in at a particular timestep.
        ///</summary>
        public override void Run()
        {
            ModelCore.UI.WriteLine("Processing landscape for hurricane events ...");

            foreach(var year in Enumerable.Range(0, this.parameters.Timestep))
            {
                int stormsThisYear = -1;
                var randomNum = PlugIn.ModelCore.GenerateUniform();
                if (HurricaneEvent.HurricaneRandomNumber)
                    randomNum = PlugIn.HurricaneGeneratorStandard.NextDouble();

                var cummProb = 0.0;
                foreach(var probability in this.parameters.StormOccurenceProbabilities)
                {
                    cummProb += probability;
                    stormsThisYear++;
                    if(randomNum < cummProb)
                        break;
                }
                string message = stormsThisYear + " storms.";
                if(stormsThisYear == 1) message = "1 storm.";
                foreach(var stormCount in Enumerable.Range(0, stormsThisYear))
                {
                    var storm = HurricaneEvent.Initiate(this.ContinentalGrid);
                    //storm.hurricaneYear = 
                    //    year + PlugIn.ModelCore.CurrentTime - 
                    //    this.parameters.Timestep;

                    storm.hurricaneNumber = stormCount+1;

                    bool impactsStudyArea = 
                        storm.GenerateWindFieldRaster(this.mapNameTemplate, 
                        PlugIn.modelCore, this.ContinentalGrid);
                    
                    LogEvent(PlugIn.ModelCore.CurrentTime, storm);
                }
            }

            WriteSummaryLog(PlugIn.modelCore.CurrentTime);
        }

        //---------------------------------------------------------------------

        enum unitType { Dist, Speed };
        private void LogEvent(int currentTime, HurricaneEvent hurricaneEvent = null)
        {
            // commented out in case I want to use it later.
            // commented out to eliminate a warning.
            //string cvtKilometersToMiles(double kValue, unitType unitType)
            //{
            //    double milesValue = kValue * 0.621371;
            //    string kUnits = unitType == unitType.Dist ? "kilometers" : "kph";
            //    string mUnits = unitType == unitType.Dist ? " miles" : " mph";
            //    return $"{kValue:F1} {kUnits} / {milesValue:F1} {mUnits}";
            //}

            eventLog.Clear();
            EventsLog el = new EventsLog();
            el.Time = currentTime;
            el.HurricaneNumber = hurricaneEvent.hurricaneNumber;
            el.ImpactsStudyArea = hurricaneEvent.studyAreaImpacts;
            el.StudyAreaMaxWS = hurricaneEvent.StudyAreaMaxWindspeed;
            el.StudyAreaMinWS = hurricaneEvent.StudyAreaMinWindspeed;
            el.LandfallLatitude = hurricaneEvent.landfallLatitude;
            el.LandfallMaxWindSpeed = hurricaneEvent.landfallMaxWindSpeed;
            el.PathHeading = hurricaneEvent.stormTrackHeading;
            eventLog.AddObject(el);
            eventLog.WriteToFile();

            if(hurricaneEvent.studyAreaImpacts)
                summaryEventCount++;
            //summaryTotalSites += hurricaneEvent.studyAreaImpacts;
        }

        //---------------------------------------------------------------------

        private void WriteSummaryLog(int currentTime)
        {
            summaryLog.Clear();
            SummaryLog sl = new SummaryLog();
            sl.Time = currentTime;
            //sl.TotalSitesDisturbed = summaryTotalSites;
            sl.NumberEvents = summaryEventCount;

            summaryLog.AddObject(sl);
            summaryLog.WriteToFile();
        }
    }

}
