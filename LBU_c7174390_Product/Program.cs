using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class analyser
{
    private static System.IO.StreamReader csv;
    private static Queue<dotnet_keylogger.keylog> keylogs = new Queue<dotnet_keylogger.keylog>();
    private static dotnet_keylogger.keylog latest_keylog;
    private static string user;
    private static string out_basedir = @"C:\kd_d_out\";
    private static Dictionary<string, FeatureQueue> FeatureQueues = new Dictionary<string, FeatureQueue>();
    private List<Session> Sessions = new List<Session>();
    private static Dictionary<int, Dictionary<String, SampleCollection>> SampleCollections = new Dictionary<int, Dictionary<String, SampleCollection>>();
    private static dotnet_keylogger.KeylogQ keylogQ;

    public analyser(dotnet_keylogger.KeylogQ kQ, string application)
    {
        keylogQ = kQ;
        this.Sessions.Add(new Session(0));
        while (keylogs.Count < 20)
        {
            // build up a queue so we can look forward
            if (!readKeylog(ProcessRegex:application))
            { throw new System.Exception("No matching keylogs to analyse"); }
        }
        while (readKeylog(ProcessRegex:application))
        {
            //continue;
            processKeylogs();
        }
        foreach (FeatureQueue fq in FeatureQueues.Values)
        {
            fq.Close();
        }
        //OutputSessions();
        //OutputApplications();
        //int[] AggRange = { 4, 8, 16, 32};
        //int[] AggRange = { 4,};
        //OutputSamples(SampleAggs:AggRange);
        //outputFeatureQueueOutlierTestStats(FeatureQueues);
    }
    public Dictionary<string, Application> getApps()
    {
        return this.Sessions[0].Applications;
    }
    private Boolean readKeylog(String ApplicationRegex = ".*", String ProcessRegex = ".*", String SessionRegex = ".*")
    {
        while (! keylogQ.isEmpty())
        {
            bool _match;
            try
            {
                latest_keylog = keylogQ.Retrieve();
                if (!this.Sessions.Last().AddKeylog(latest_keylog, user))
                {
                    // Session wants to be closed, time for a new one
                    this.Sessions.Add(new Session(Sessions.Count));
                    this.Sessions.Last().AddKeylog(latest_keylog, user);
                }
                _match = true;
                if (SessionRegex != ".*")
                {
                    if (!(Regex.Match(latest_keylog.Session.ToString(), SessionRegex)).Success)
                    {
                        _match = false;
                    }
                }
                if (ApplicationRegex != ".*")
                {
                    if (!(Regex.Match(latest_keylog.WindowName, ProcessRegex)).Success)
                    {
                        _match = false;
                    }
                }
                if (ProcessRegex != ".*")
                {
                    if (!(Regex.Match(latest_keylog.WindowTitle.ToString(), ProcessRegex)).Success)
                    {
                        _match = false;
                    }
                }
                if (_match)
                {
                    keylogs.Enqueue(latest_keylog);
                    return true;  // We have an event, time to go
                }
            }
            catch
            { 
                //ignore
            }
        }
        return false; // Couldn't find an event, time to return false
    }
    private static void processKeylogs()
    {
        dotnet_keylogger.keylog[] a_keylogs = keylogs.ToArray();
        switch (a_keylogs[0].Raw_flags >> 7)
        {
            case 1:
                // keyup
                break;
            case 0:
                processKD(a_keylogs); // disable for outlier tests
                //testOutlierThreshold(a_keylogs); //enable for outlier tests
                // keydown
                break;
            default:
                //error
                break;
        }
        keylogs.Dequeue();
    }
    private static String FeatureToLine(dotnet_keylogger.keylog keylog, string feature, int seqNo)
    {
        return String.Join(dotnet_keylogger.keylog.delimeter.ToString(), new string[] { keylog.Timestamp.ToString("o"), user, seqNo.ToString(), keylog.Key, feature});
    }
    private static void outputFeatureQueueStats(Dictionary<string, FeatureQueue> FQs)
    {
        String Header = "Feature, FeatureCount, OutlierCount, OutlierPercentage";
        System.IO.StreamWriter SW = new System.IO.StreamWriter(out_basedir + "Stats.CSV");
        SW.WriteLine(Header);
        foreach (FeatureQueue FQ in FQs.Values)
        {
            SW.WriteLine(String.Join(",",
                FQ.Name,
                FQ.RegisteredKeylogs.ToString(),
                FQ.OutlierCount.ToString(),
                ((100 / FQ.RegisteredKeylogs) * FQ.OutlierCount).ToString()
                ));
        }
        SW.Flush();
    }
    private void OutputSessions()
    {
        this.Sessions.Last().Close();
        System.IO.StreamWriter SW = new System.IO.StreamWriter(out_basedir + "Sessions.CSV");
        SW.WriteLine(Session.Header);
        foreach (Session S in this.Sessions)
        {
            SW.WriteLine(S.Print());
        }
        SW.Flush();
    }
    private void OutputApplications()
    {
        System.IO.StreamWriter SW = new System.IO.StreamWriter(out_basedir + "Applications.CSV");
        SW.WriteLine(Session.ApplicationsHeader);
        foreach (Session S in this.Sessions)
        {
            SW.WriteLine(S.PrintApplications());
        }
        SW.Flush();
    }
    public void OutputSamples(string user, string application, int MinSample = 10, int SampleAgg = 0, int[] SampleAggs = null, string path = null)
    {
        if (path == null)
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\";
        }
        if(SampleAgg == 0 && SampleAggs == null)
        {
            throw new System.Exception("SampleAgg or SampleAggs required");
        }
        if (SampleAgg != 0 && SampleAggs != null)
        {
            throw new System.Exception("Only one of SampleAgg or SampleAggs required");
        }
        //Calculate minimum samples needed to satisfy MinSample number
        int MinimumSamplesRequired = MinSample * (SampleAgg == 0 ? SampleAggs.Min() : SampleAgg);
        this.Sessions.Last().Close();
        foreach (int Session in SampleCollections.Keys)
        {
            /// Build the statstable header
            String StatsTable = "FeatureName, FeatureCount, ";
            if (SampleAggs != null)
            {
                foreach (int SampleSize in SampleAggs)
                {
                    StatsTable += (SampleSize * MinSample).ToString() + ": Midpoint,";
                    StatsTable += (SampleSize * MinSample).ToString() + ": Variance,";
                }
                StatsTable.TrimEnd(',');
                StatsTable += Environment.NewLine;
            }
            foreach (SampleCollection SC in SampleCollections[Session].Values)
            {
                SC.Close();
                if (!SampleCollection.IsTooSmall(SC,MinimumSamplesRequired )) //ignore small samplescollections
                {
                    System.IO.StreamWriter SW = new System.IO.StreamWriter(path +
                                                    $"{user}_{application}_Samples_{SC.FeatureName}.csv");
                    switch(SampleAgg)
                    {
                        case 0:
                            //not define, send the int range
                            //+ make a stats table!
                            StatsTable += SC.PrintStats(SampleAggs);
                            SW.Write(SC.Print("CSV", SampleAggs));
                            break;
                        case 1:
                            //no agg needed
                            SW.Write(SC.Print("CSV"));
                            break;
                        default:
                            //agg away
                            SW.Write(SC.Print("CSV", SampleAgg));
                            break;
                    }
                        SW.Flush();
                    SW.Close();

                }
            }
            if (SampleAggs != null)
            {
                System.IO.StreamWriter ST = new System.IO.StreamWriter(path +
                                                        $"{user}_{application}_StatsTable.csv");
                ST.Write(StatsTable);
                ST.Flush();
                ST.Close();
            }
        }
    }
    private static void processKD(dotnet_keylogger.keylog[] a_keylogs)
    {
        int next_kd = findNextOccurance("kd", a_keylogs, 0);
        if (a_keylogs[0].IsCapital && false) //disabled
        {
            if ((a_keylogs[0].Flags & (byte)0b0000_0110) > 0)
            {
                Feature ShiftPref;
                Feature ShiftDwell;
                int ShiftRelease = 0;
                //shift key depressed
                switch ((a_keylogs[0].Flags & (byte)0b0000_0110))
                {
                    case 2:
                        //right shift
                        ShiftPref = new Feature(1, a_keylogs[0], user);
                        FeatureQueueHelper.ProcessFeature(FeatureQueues, ShiftPref, "ShiftPref", true, false, false, out_basedir, SampleCollections, OutlierSampleSize: 99999);
                        ShiftRelease = findNextOccurance("ku", a_keylogs, 0, 161);
                        break;
                    case 4:
                        //left shift
                        ShiftPref = new Feature(0, a_keylogs[0], user);
                        FeatureQueueHelper.ProcessFeature(FeatureQueues, ShiftPref, "ShiftPref", true, false, false, out_basedir, SampleCollections, OutlierSampleSize:99999);
                        ShiftRelease = findNextOccurance("ku", a_keylogs, 0, 160);
                        break;
                    case 6:
                        // both shifts
                        break;
                    default:
                        // unexpected
                        break;
                }
                if (!a_keylogs[findNextOccurance("kd", a_keylogs, 1)].IsCapital)
                {
                    // Next KD is not capital, this must be the last shifted keydown
                    if (ShiftRelease != 0)
                    {
                        ShiftDwell = new Feature((a_keylogs[ShiftRelease].Timestamp - a_keylogs[0].Timestamp).TotalSeconds, a_keylogs[ShiftRelease], user);
                        FeatureQueueHelper.ProcessFeature(FeatureQueues, ShiftDwell, "ShiftDwell", true, true, true, out_basedir, SampleCollections, OutlierSampleSize: 99999);
                    }
                }
            }
            if ((a_keylogs[0].Flags & (byte)0b0010_0000) > 0)
            {
                //capslock - not sure how to implement
                //ShiftPreference = "capslock";
            }

        }
        if (next_kd > 0)
        {
            int this_ku = findNextOccurance("ku", a_keylogs, 0, a_keylogs[0].vkCode); // matches current kd
            int next_ku = findNextOccurance("ku", a_keylogs, 0); // generic
            // if we found another KD in the sample
            if (a_keylogs[next_kd].vkCode == a_keylogs[0].vkCode)
            {
                // Double pressed key
                Feature dk_kd_kd = new Feature((a_keylogs[next_kd].Timestamp - a_keylogs[0].Timestamp).TotalMilliseconds, a_keylogs[0], user);
                FeatureQueueHelper.ProcessFeature(FeatureQueues, dk_kd_kd, "dk-kd-kd", false, false, false, out_basedir, SampleCollections);
                Feature dk_kd_ku = new Feature((a_keylogs[this_ku].Timestamp - a_keylogs[0].Timestamp).TotalMilliseconds, a_keylogs[0], user);
                FeatureQueueHelper.ProcessFeature(FeatureQueues, dk_kd_ku, "dk-kd-ku", false, false, false, out_basedir, SampleCollections);
                int second_ku = findNextOccurance("ku", a_keylogs, this_ku, a_keylogs[0].vkCode);
                Feature dk_kd_ku_ku = new Feature((a_keylogs[second_ku].Timestamp - a_keylogs[0].Timestamp).TotalMilliseconds, a_keylogs[0], user);
                FeatureQueueHelper.ProcessFeature(FeatureQueues, dk_kd_ku_ku, "dk-kd-ku-ku", false, false, false, out_basedir, SampleCollections);
            }
            if (a_keylogs[0].vkCode == 160 || a_keylogs[0].vkCode == 161 && false)
            {
                // We are processing a shift key down
                Feature ShiftLatency = new Feature((a_keylogs[next_kd].Timestamp - a_keylogs[0].Timestamp).TotalSeconds, a_keylogs[0], user);
                FeatureQueueHelper.ProcessFeature(FeatureQueues, ShiftLatency, "ShiftLatency", true, true, true, out_basedir, SampleCollections );
            }
            Feature dwell = new Feature((double)(a_keylogs[this_ku].Timestamp - a_keylogs[0].Timestamp).TotalMilliseconds, a_keylogs[0], user);
            FeatureQueueHelper.ProcessFeature(FeatureQueues, dwell, "dwell", false, false, false, out_basedir, SampleCollections);
            Feature latency = new Feature ((double)(a_keylogs[next_kd].Timestamp - a_keylogs[this_ku].Timestamp).TotalMilliseconds, a_keylogs[this_ku], user);
            FeatureQueueHelper.ProcessFeature(FeatureQueues, latency, "latency", false, false, false, out_basedir, SampleCollections);
            //Feature approach = new Feature((double)(a_keylogs[next_kd].Timestamp - a_keylogs[this_ku].Timestamp).TotalMilliseconds, a_keylogs[next_kd], user); // Key[N+1]
            //FeatureQueueHelper.ProcessFeature(FeatureQueues, approach, "approach", true, true, true, out_basedir, SampleCollections);
        }
    }
    public static int findNextOccurance(String attribute, dotnet_keylogger.keylog[] a_keylogs, int offset)
    {
        /// need to filter otu shift, alt, win etc
        switch(attribute)
        {
            case "kd":
                while (offset < (a_keylogs.Length - 1))
                {
                    offset++;
                    if ((a_keylogs[offset].Raw_flags >> 7) == 0)
                    {
                        return offset;
                    }
                }
                return 0;
            case "ku":
                while (offset < (a_keylogs.Length - 1))
                {
                    offset++;
                    if ((a_keylogs[offset].Raw_flags >> 7) == 1)
                    {
                        return offset;
                    }
                }
                return 0;
            default:
                return 0;
        }
    }
    public static int findNextOccurance(String attribute, dotnet_keylogger.keylog[] a_keylogs, int offset, int vkCode)
    {
        switch (attribute)
        {
            case "kd":
                while (offset < (a_keylogs.Length - 1))
                {
                    offset++;
                    if ((a_keylogs[offset].Raw_flags >> 7) == 0 && a_keylogs[offset].vkCode == vkCode)
                    {
                        return offset;
                    }
                }
                return 0;
            case "ku":
                while (offset < (a_keylogs.Length - 1))
                {
                    offset++;
                    if ((a_keylogs[offset].Raw_flags >> 7) == 1 && a_keylogs[offset].vkCode == vkCode)
                    {
                        return offset;
                    }
                }
                return 0;
            default:
                return 0;
        }
    }
}

class Feature
{
    public dotnet_keylogger.keylog Keylog { get; set; }
    public double Value { get; }
    public bool IsOutlier { get; set; }
    public double SeqNo { get; set; } = 0;
    public string User { get; set; }
    public Feature (double Value, dotnet_keylogger.keylog Keylog, string User)
    {
        this.Value = Value;
        this.Keylog = Keylog;
        this.User = User;
    }
    public string ToCSV()
    {
        return String.Join(dotnet_keylogger.keylog.delimeter.ToString(), new string[] { this.Keylog.Timestamp.ToString("o"), this.User, this.SeqNo.ToString(), this.Keylog.Key, this.Value.ToString() });
    }
}

class FeatureQueue
{
    public string Name { get; set; }
    public Queue<Feature> FeatureQ { get; } = new Queue<Feature>();
    public System.IO.StreamWriter SW { get; set; }
    public System.IO.StreamWriter DeviationSW { get; set; }
    public string OutputFormat { get; set; }
    public string OutputPath { get; set; }
    public string CSVHeader = string.Join(dotnet_keylogger.keylog.delimeter.ToString(), new string[] { "time", "user", "seqNo", "key" });
    public string DeviationCSVHeader = string.Join(dotnet_keylogger.keylog.delimeter.ToString(), new string[] { "time", "user", "seqNo" });
    public bool UseSW { get; set; } = false;
    public bool TrackMAD { get; set; } = false;
    public double[] Values { get; set; }
    public int OutlierSampleSize { get; set; } = 20; // set real high to prevent outlier removal
    public float MADtolerance { get; set; }
    public double MAD { get; set; }
    public double Median { get; set; }
    public double OutlierCount { get; set; } = 0;
    public double SeqNo { get; set; } = 0;
    public double RegisteredKeylogs { get; set; } = 0;
    public bool BatchProcessingDone { get; set; } = false;
    public int LastObservedSession { get; set; } = -1;
    public bool TrackOutliers { get; set; }
    public int OutlierTrackingInterval { get; set; }
    public Queue<Feature> OutlierTracker { get; set; } = new Queue<Feature>();
    List<float> OutliersAverages = new List<float>();
    public Dictionary<string, System.IO.StreamWriter> StreamWriters = new Dictionary<String, System.IO.StreamWriter>();
    public Dictionary<int, Dictionary<String,SampleCollection>> SampleCollections;
    public SampleCollection CurrentSampleCollection;
    public SampleCollection CurrentMADSampleCollection;
    public SampleCollection CurrentOutlierSampleCollection;
    public void ProcessFeature(Feature F)
    {
        if (TestAgainstMAD(F))
        {
            this.SeqNo++;
            F.IsOutlier = false;
            F.SeqNo = this.SeqNo;
            this.OutputFeature(F);
        }
        else
        {
            F.IsOutlier = true; this.OutlierCount++;
        }
        if (this.TrackOutliers) { this.OutlierTracker.Enqueue(F); this.ProcessOutliers(); }
    }
    public void FeatureEnqueue(Feature F)
    {
        if(this.LastObservedSession == -1)
        {
            this.LastObservedSession = F.Keylog.Session;
            this.GetSampleCollections();
        }
        if (F.Keylog.Session != this.LastObservedSession)
        {
            //New session, old features no longer relevant for outliers
            if(! this.BatchProcessingDone)
            {
                this.ProcessBacklog();
            }
            this.FeatureQ.Clear();
            this.BatchProcessingDone = false;
            this.LastObservedSession = F.Keylog.Session;
            this.GetSampleCollections();
        }
        this.FeatureQ.Enqueue(F);
        this.RegisteredKeylogs++;
        this.Values = FeatureQ.ToArray().Select(x => x.Value).ToArray();
        var MAD_return = MedianHelper.FindMAD(this.Values);
        this.MAD = MAD_return.Item1;
        this.Median = MAD_return.Item2;
        if (this.FeatureQ.Count() == this.OutlierSampleSize)
        {
            if (this.BatchProcessingDone)
            {
                // We have already processed the sample so just do this latest feature and move on
                ProcessFeature(F);
            }
            else
            {
                // We need to process all the Features in the queue
                this.ProcessBacklog();
            }
            if (this.TrackMAD == true && this.BatchProcessingDone)
            {
                // We are tracking deviation and batchprocessing is done so sampelsize reached
                this.OutputMAD();               
            }
            this.FeatureQ.Dequeue();
        }
    }
    public void FeatureEnqueue(Feature[] Fa)
    {
        foreach (Feature F in Fa)
        { this.FeatureEnqueue(Fa); }
        /*if (F.Count() > this.OutlierSampleSize)
        { throw new SystemException("You have tried to ingest too many samples"); }
        // if an array is provided, ingest it all before calculating outliers
        foreach (Feature Fi in F)
        {
            this.FeatureQ.Enqueue(Fi);
            if (this.FeatureQ.Count() > this.OutlierSampleSize)
            { this.FeatureQ.Dequeue(); }
        }
        this.Values = FeatureQ.ToArray().Select(x => x.Value).ToArray();
        var MAD_return = MedianHelper.FindMAD(this.Values);
        this.MAD = MAD_return.Item1;
        this.Median = MAD_return.Item2;
        foreach (Feature Fi in F)
        {
            if (TestAgainstMAD(Fi))
            {
                this.SeqNo++;
                Fi.IsOutlier = false;
                Fi.SeqNo = this.SeqNo;
            }
            else { Fi.IsOutlier = true; this.OutlierCount++; }
        }*/
    }
    public void ProcessBacklog()
    {
        foreach (Feature backloggedFeature in FeatureQ)
        {
            ProcessFeature(backloggedFeature);
        }
        this.BatchProcessingDone = true;
    }
    public void GetSampleCollections()
    {
        this.CurrentSampleCollection = SampleCollection.GetSampleCollection(
                                this.Name, this.LastObservedSession, this.SampleCollections);
        if (this.TrackMAD)
        {
            this.CurrentMADSampleCollection = SampleCollection.GetSampleCollection(
                                          $"{this.Name}_MAD", this.LastObservedSession, this.SampleCollections);
        }
        if (this.TrackOutliers)
        {
            this.CurrentOutlierSampleCollection = SampleCollection.GetSampleCollection(
                            $"{this.Name}_Outliers", this.LastObservedSession, this.SampleCollections, samplesize:this.OutlierTrackingInterval, Fake:true);
        }

    }
    public FeatureQueue(string Name, int OutlierSampleSize, float MAD_tolerance, string OutputFormat, bool track_MAD, 
        bool TrackOutliers, string OutputDir, Dictionary<int, Dictionary<String, SampleCollection>> SampleCollections, 
        int OutlierTrackingInterval=40)
    {
        this.Name = Name;
        this.OutlierSampleSize = OutlierSampleSize;
        this.MADtolerance = MAD_tolerance;
        this.Name = Name;
        this.OutlierSampleSize = OutlierSampleSize;
        this.MADtolerance = MAD_tolerance;
        this.TrackMAD = track_MAD;
        this.TrackOutliers = TrackOutliers;
        this.OutlierTrackingInterval = OutlierTrackingInterval;
        this.OutputFormat = OutputFormat;
        this.SampleCollections = SampleCollections;
        switch (this.OutputFormat)
        {
            case "CSV":
                this.OutputPath = OutputDir + @"\{0}.csv";
                break;
        }
    }
    public void Close()
    {
        if (this.OutputFormat != "")
        {
            if (!this.BatchProcessingDone)
            {
                foreach (Feature backloggedFeature in FeatureQ)
                {
                    ProcessFeature(backloggedFeature);
                }
            }
        }
        this.OutputOutliers();
        if (this.OutputFormat == "CSV")
        { 
            foreach (System.IO.StreamWriter SW in this.StreamWriters.Values)
            {
                SW.Flush();
                SW.Close();
            }
        }
    }
    public double Count()
    {
        return FeatureQ.Count();
    }
    public bool TestAgainstMAD(Feature F)
    {
        if (this.Count() < this.OutlierSampleSize) return true; // Not enough of a sample to identify outlier
        if (F.Value > (this.Median + (this.MADtolerance * this.MAD)))
        {
            /* discard if latest is greater than median plus mad
             * latest = 10, median = 6, threshold = 3. DISCARD
             */
            return false;
        }
        if (F.Value < (this.Median - (this.MADtolerance * this.MAD)))
        {
            /* discard if latest is less than median minus mad
             * latest = 2, median = 6, threshold = 3. DISCARD
             */
            return false;
        }
        return true; // store the badger
    }
    public void ProcessOutliers()
    {
        if(OutlierTracker.Count < this.OutlierTrackingInterval) { return; }
        List<int> Outliers = new List<int>();
        DateTime StartTime = new DateTime();
        while (this.OutlierTracker.Count > 0)  // Loop process samples
        {
            if((Outliers.Count + this.OutlierTracker.Count) < this.OutlierTrackingInterval) { return; } // not enough for another sample
            Feature outlier = this.OutlierTracker.Dequeue();
            if (StartTime == DateTime.MinValue)
            {
                StartTime = outlier.Keylog.Timestamp;
            }
            Outliers.Add(outlier.IsOutlier ? 1:0); // 1 if outlier
            if (Outliers.Count == OutlierTrackingInterval)
            {
                Tuple<double,double> ret = MeanHelper.FindMAD(Outliers.ToArray());
                OutliersAverages.Add((float)ret.Item1);
                //Build our own sample as this feature is derived from a feature, not a keylog
                Sample FakeSample = new Sample($"{this.Name}_Outliers",ret.Item1, ret.Item2, StartTime, outlier.Keylog.Timestamp, OutliersAverages.Count(), OutlierTrackingInterval);
                this.CurrentOutlierSampleCollection.AddFakeSample(FakeSample);
                Outliers.Clear();
                StartTime = DateTime.MinValue;
            }
        }
    }
    public void OutputOutliers()
    {
        if(OutliersAverages.Count == 0) { return; }
        switch (this.OutputFormat)
        {
            case "CSV":
                if (!this.StreamWriters.ContainsKey("OutlierSW"))
                {
                    this.StreamWriters.Add("OutlierSW", new System.IO.StreamWriter(String.Format(this.OutputPath, $"{this.Name}_Outliers")));
                    this.StreamWriters["OutlierSW"].WriteLine("Index,OutlierAverage");
                }
                for (int i = 0; i < OutliersAverages.Count; i++)
                {
                    this.StreamWriters["OutlierSW"].WriteLine($"{i},{OutliersAverages[i]}");
                }
                break;
        }

    }
    public void OutputFeature(Feature F)
    {
        this.CurrentSampleCollection.ProcessFeature(F);
        // We have to use F as the .Last() trick doesnt work for backlog
        switch (this.OutputFormat)
            {
                case "CSV":
                    if (!this.StreamWriters.ContainsKey("Features"))
                        {
                            this.StreamWriters.Add("Features", new System.IO.StreamWriter(String.Format(this.OutputPath, this.Name)));
                            this.StreamWriters["Features"].WriteLine(this.CSVHeader + dotnet_keylogger.keylog.delimeter.ToString() + this.Name);
                        }
                    StreamWriters["Features"].WriteLine(F.ToCSV());
                    break;
            }

    }
    public void OutputMAD()
    {
        //how do we handle MAD Samples?
        this.CurrentMADSampleCollection.ProcessFeature(FeatureQ.Last().Keylog.Timestamp,this.MAD);
        switch (this.OutputFormat)
        {
            case "CSV":
                if (!this.StreamWriters.ContainsKey("MADSW"))
                {
                    this.StreamWriters.Add("MADSW", new System.IO.StreamWriter(String.Format(this.OutputPath, $"{this.Name}_MAD")));
                    this.StreamWriters["MADSW"].WriteLine(this.DeviationCSVHeader + dotnet_keylogger.keylog.delimeter.ToString() + this.Name + "_MAD");
                }
                string line = (String.Join(dotnet_keylogger.keylog.delimeter.ToString(), new string[]
                    { FeatureQ.Last().Keylog.Timestamp.ToString("o"), FeatureQ.Last().User, this.RegisteredKeylogs.ToString(), this.MAD.ToString()}));
                this.StreamWriters["MADSW"].WriteLine(line);
                break;
        }
    }
}

class FeatureQueueHelper
{
    private static void AddToQueue(Dictionary<string, FeatureQueue> FeatureQueues, Feature Feature,
                                    String FeatureName, bool TrackMAD, bool TrackOutliers, String OutputDirectory,
                                    int OutlierSampleSize, int MADTolerance, 
                                    Dictionary<int, Dictionary<String, SampleCollection>> SampleCollections, String OutputFormat)
    {
        if (!FeatureQueues.ContainsKey(FeatureName))
        { // FeatureQueue does not yet exist
            FeatureQueues.Add(FeatureName, new FeatureQueue(FeatureName, OutlierSampleSize, MADTolerance, OutputFormat, TrackMAD,
                TrackOutliers, OutputDirectory, SampleCollections));
        }
         FeatureQueues[FeatureName].FeatureEnqueue(Feature);
    }
    public static void ProcessFeature(Dictionary<string, FeatureQueue> FeatureQueues, Feature Feature, 
                                    String FeatureName, bool perVK, bool TrackMAD, bool TrackOutliers, String OutputDirectory, 
                                    Dictionary<int, Dictionary<String, SampleCollection>> SampleCollections,
                                    int OutlierSampleSize = 160, int MADTolerance = 3, String OutputFormat = "NA")
    {
        AddToQueue(FeatureQueues, Feature, FeatureName, TrackMAD, TrackOutliers, OutputDirectory, OutlierSampleSize, MADTolerance,
           SampleCollections, OutputFormat);
        if (perVK)
        {
            String VkCodeFeatureName = $"{FeatureName}_vk{Feature.Keylog.vkCode}";
            AddToQueue(FeatureQueues, Feature, VkCodeFeatureName, TrackMAD, TrackOutliers, OutputDirectory, OutlierSampleSize / 2,
                MADTolerance, SampleCollections, OutputFormat);
        }
    }
}

class Session
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<dotnet_keylogger.keylog> Keylogs { get; set; }
    public Dictionary<String, Application> Applications = new Dictionary<string, Application>();
    public int ApplicationCount { get; set; } = 0;
    public static String Header { get; } = "SessionIndex,User,StartTime,EndTime,Duration(s),KeylogCount,KeylogVelocity," +
                                           "ApplicationCount";
    public static String ApplicationsHeader { get; } = "SessionIndex,ApplicationName,StartTime,EndTime,Duration(s)," +
                                              "KeylogCount,KeylogVelocity";
    public string User { get; set; }
    public bool Closed { get; set; } = false;
    public int KeylogCount { get; set; } = 0;
    public int MaxAge { get; set; } = 0;
    public int Index { get; set; }
    public double Duration { get; set; }
    public double KeylogVelocity { get; set; } = 0;
    public Session (int Index, int MaxAge = 3600)
    {
        this.MaxAge = MaxAge;
        this.Index = Index;
    }
    public void Close()
    {
        this.EndTime = this.Keylogs.Last().Timestamp;
        this.Duration = (this.EndTime - this.StartTime).TotalSeconds;
        this.KeylogVelocity = this.KeylogCount / this.Duration;
        foreach (String ApplicationName in Applications.Keys)
        {
            Applications[ApplicationName].Close();
        }
    }
    public bool ContainsApplication(string Name)
    {
        if (this.Applications.ContainsKey(Name))
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    public bool AddKeylog(dotnet_keylogger.keylog Keylog, string User)
    {
        if(this.Closed) { throw new SystemException("Session is closed"); }
        if (this.Keylogs != null)
        {
            if ((Keylog.Timestamp - this.Keylogs.Last().Timestamp).TotalSeconds > this.MaxAge)
            {
                // Too old, time to close
                this.Close();
                return false;
            }
            if (User != this.User)
            {
                // Diff user, time to close
                this.Close();
                return false;
            }
        }
        else
        {
            this.Keylogs = new List<dotnet_keylogger.keylog>();
            this.StartTime = Keylog.Timestamp;
            this.User = User;
        }
        this.Keylogs.Add(Keylog);
        this.KeylogCount++;
        Keylog.Session = this.Index;
        if (!this.ContainsApplication(Keylog.WindowTitle))
        {
            this.Applications.Add(Keylog.WindowTitle, new Application(Keylog.WindowTitle));
            this.ApplicationCount++;
        }
        this.Applications[Keylog.WindowTitle].ProcessKeylog(Keylog);
        return true;
    }
    public string Print()
    {
        return $"Session_{this.Index},{this.User},{this.StartTime.ToString("o")},{this.EndTime.ToString("o")}," +
            $"{Math.Round(this.Duration)},{this.KeylogCount},{Math.Round(this.KeylogVelocity,1)},{this.ApplicationCount}";
    }
    public string PrintApplications()
    {
        string ret = "";
        foreach (Application A in Applications.Values)
        {
            ret += $"{Index},{A.Name},{A.StartTime.ToString("o")},{A.EndTime.ToString("o")}," +
                $"{Math.Round(A.Duration)},{A.KeylogCount}, {Math.Round(A.KeylogVelocity,2)}" +
                Environment.NewLine;
        }
        return ret;
    }
}

class Application
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int KeylogCount { get; set; } = 0;
    public double KeylogVelocity { get; set; } = 0;
    public double Duration { get; set; } //this is nonesense as applications overlap
    public dotnet_keylogger.keylog LastKeylog { get; set; }
    public string Name { get; set; }

    public Application(String Name)
    {
        this.Name = Name;
    }
    public void ProcessKeylog(dotnet_keylogger.keylog K)
    {
        if (this.Name != K.WindowTitle)
        { throw new System.Exception("Keylog application name does not match Application obj name"); }
        if (this.StartTime == DateTime.MinValue) { this.StartTime = K.Timestamp; }
        this.EndTime = K.Timestamp;
        this.KeylogCount++;
    }
    public void Close()
    {
        this.Duration = (this.EndTime - this.StartTime).TotalSeconds;
        this.KeylogVelocity =  this.KeylogCount / this.Duration;
    }
}

class MedianHelper
{
    public static double FindMedian(double[] A)
    {
        if (A.Length == 0)
        {
            throw new System.Exception("Zero length array passed to Median calculation");
        }
        if (A.Length == 1)
        {
            return A[0];
        }
        Array.Sort(A);
        if (A.Length % 2 == 0)
        {
            /* 
             * Array count is even
             * 1 2 3 4
             *    ^
             * Remember, Array starts at zero so its between 1 and 2, not 2 and 3
             */

            return (A[A.Length / 2] + A[A.Length / 2 - 1]) / 2;
        }
        else
        {
            /* MAD_min_features is odd
             * 1 2 3
             *   ^
             * Remember, Array starts at zero so its between 1 and 2, not 2 and 3
             */
            return A[(A.Length - 1) / 2];
        }
    }
    public static Tuple<double, double> FindMAD(double[] A)
    {
        double median = FindMedian(A);
        double[] B = (double[]) A.Clone();
        for (int i = 0; i < A.Length; i++)
        {
            B[i] = System.Math.Abs(A[i] - median);
        }
        // A is now an array of deviation from median
        double MAD = FindMedian(B);
        return Tuple.Create(MAD, median);
    }
}

class MeanHelper
{
    public static double FindMean(int[] A)
    {
        return FindMean(A.Select(x => (double)x).ToArray());
    }
    public static double FindMean(double[] A)
    {
        if(A.Length == 0)
        {
            throw new System.Exception("Zero length array passed to Mean calculation");
        }
        if(A.Length == 1)
        {
            return A[0];
        }
        double sum = A.Sum();
        double count = A.Count();
        double mean = (double) sum / count;
        return mean;
    }
    public static Tuple<double, double> FindMAD(int[] A)
    {
        return FindMAD(A.Select(x => (double)x).ToArray());
    }
    public static Tuple<double, double > FindMAD(double[] A)
    {
        double mean = FindMean(A);
        double[] B = (double[])A.Clone();
        for (int i = 0; i < A.Length; i++)
        {
            B[i] = System.Math.Abs(A[i] - mean);
        }
        // A is now an array of deviation from mean
        double MAD = FindMean(B);
        return Tuple.Create(MAD, mean);
    }
}

class Sample
{
    public int SampleSize { get; set; }
    public string OutputFormat { get; }
    public List<double> Features { get; set; } = new List<double>();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double MAD { get; set; }
    public double Mean { get; set; }
    public string FeatureName { get; set; }
    public int SampleID { get; set; } = 0;
    public bool Faked { get; } = false;
    public bool Closed { get; set; }
    public Sample(int SampleSize, int SampleID)
    {
        this.SampleID = SampleID;
        this.SampleSize = SampleSize;
    }
    public Sample(string FeatureName, double MAD, double Mean, DateTime StartTime, DateTime EndTime, int SampleID, int SampleSize)
    {
        this.FeatureName = FeatureName;
        this.MAD = MAD;
        this.Mean = Mean;
        this.Faked = true;
        this.StartTime = StartTime;
        this.EndTime = EndTime;
        this.SampleID = SampleID;
        this.SampleSize = SampleSize;
        this.Closed = true;
    }
    public void Close()
    {
        if (this.Closed) { return; }
        if (this.Faked)
        {
            this.Closed = true;
        }
        if (!this.Faked && this.Features.Count == this.SampleSize)
        {
            this.ProcessFeatures();
            this.Closed = true;
        }
        if (!this.Faked && this.Features.Count != this.SampleSize)
        {
            this.Closed = false;
        }
        
    }
    public static bool IsOpen(Sample S) { return S.Closed ? false : true; }
    public bool AddFeature(Feature F)
    {
        if(this.StartTime == DateTime.MinValue) { this.StartTime = F.Keylog.Timestamp; }
        if(this.Features.Count() == this.SampleSize)
        {
            this.Close();
            //Sample full
            return false;
        }
        else
        {
            this.Features.Add(F.Value);
            this.EndTime = F.Keylog.Timestamp;
            return true;
        }
    }
    public bool AddValue(DateTime Timestamp, double Value)
    {
        if (this.StartTime == DateTime.MinValue) { this.StartTime = Timestamp; }
        if (this.Features.Count() == this.SampleSize)
        {
            this.Close();
            //Sample full
            return false;
        }
        else
        {
            this.EndTime = Timestamp;
            this.Features.Add(Value);
            return true;
        }

    }
    public void ProcessFeatures()
    {
        Tuple<double, double> ret = MeanHelper.FindMAD(this.Features.ToArray());
        this.MAD = ret.Item1;
        this.Mean = ret.Item2;
    }
}

class SampleCollection
{
    public string FeatureName { get; set; }
    public int SampleSize { get; set; }
    List<Sample> Samples { get; set; } = new List<Sample>();
    String CSV_Header = "FeatureName, Index, AggregationCount, TrueIndex, Mean, MAD";
    public static SampleCollection GetSampleCollection(string name, int session, Dictionary<int, Dictionary<String, SampleCollection>> SCs, int samplesize = 10, bool Fake = false)
    {
        if (!SCs.ContainsKey(session))
        {
            // This session has not been registered yet
            SCs.Add(session, new Dictionary<string, SampleCollection>());
        }
        if (!SCs[session].ContainsKey(name))
        {
            // This session has not been registered yet
            SCs[session].Add(name, new SampleCollection(name, SampleSize:samplesize, Fake:Fake));
        }
        return SCs[session][name];
    }
    public SampleCollection(string FeatureName, int SampleSize, bool Fake)
    {
        this.FeatureName = FeatureName;
        this.SampleSize = SampleSize;
        if(! Fake){this.Samples.Add(new Sample(this.SampleSize, 1));} //dont create an emtpy sample for a fake colllection
    }
    public void Close()
    {
        if (this.Samples.Count > 0)
        {
            this.Samples.Last().Close(); // attempt close of last session
        }
        Samples.RemoveAll(Sample.IsOpen);
    }
    public void ProcessFeature(Feature F)
    {
        if(! Samples.Last().AddFeature(F))
        {
            // Sample returned false so must be full
            Samples.Add(new Sample(this.SampleSize, Samples.Count() + 1));
            Samples.Last().AddFeature(F);
        }
    }
    public void ProcessFeature(DateTime Timestamp, double Value)
    {
        if (!Samples.Last().AddValue(Timestamp, Value))
        {
            // Sample returned false so must be full
            Samples.Add(new Sample(this.SampleSize, Samples.Count() + 1));
            Samples.Last().AddValue(Timestamp, Value);
        }
    }
    public void AddFakeSample(Sample FS)
    {
        if(FS.SampleSize != this.SampleSize)
        {
            throw new System.Exception("inconsistent sample size");
        }
        if (FS.FeatureName != this.FeatureName)
        {
            throw new System.Exception("inconsistent feature name");
        }
        Samples.Add(FS);
    }
    public static bool IsTooSmall(SampleCollection SC, int minSize)
    {
        if(SC.Samples.Count() < minSize)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    public String Print(string format, int SessionAggregationCount, bool fromRange=false)
    {
        String ret = "";
        if (! fromRange)
        {
            this.Close();
            ret = CSV_Header + Environment.NewLine;
        }
        if (SessionAggregationCount > this.Samples.Count()) { return ""; } // Too big!
        List<Sample> buff = new List<Sample>();
        List<Sample> aggregatedSamples = new List<Sample>();

        foreach (Sample sample in Samples)
        {
            buff.Add(sample);
            if (buff.Count == SessionAggregationCount)
            {
                Sample Agg = new Sample(sample.FeatureName,
                    MeanHelper.FindMean((buff.ToArray().Select(x => x.MAD)).ToArray()),
                    MeanHelper.FindMean((buff.ToArray().Select(x => x.Mean)).ToArray()),
                    buff.First().StartTime,
                    buff.Last().EndTime,
                    buff.Last().SampleID / SessionAggregationCount,
                    buff.Last().SampleSize * SessionAggregationCount
                    );
                aggregatedSamples.Add(Agg);
                buff.Clear();
            }
        }
        foreach (Sample Sample in aggregatedSamples)
        {
            ret += $"{this.FeatureName},{Sample.SampleID},{SessionAggregationCount},{Sample.SampleID * SessionAggregationCount},{Sample.Mean},{Sample.MAD}";
            ret += Environment.NewLine;
        }
        return ret;
    }
    public String Print(string format, bool fromRange = false)
    {
        String ret = "";
        if (!fromRange)
        {
            this.Close();
            ret = CSV_Header + Environment.NewLine;
        }
        foreach (Sample Sample in Samples)
        {
            ret += $"{this.FeatureName},{Sample.SampleID},1,{Sample.SampleID},{Sample.Mean},{Sample.MAD}";
            ret += Environment.NewLine;
        }
        return ret;
    }
    public String Print(string format, int[] SessionAggregationCount)
    {
        this.Close();
        String ret = this.CSV_Header + Environment.NewLine;
        foreach (int i in SessionAggregationCount)
        {
            switch(i)
            {
                case 0:
                    break;
                case 1:
                    ret += this.Print(format,fromRange:true);
                    break;
                default:
                    ret += this.Print(format, i, fromRange: true);
                    break;
            }
        }
        // do some looping but with something clever about the header
        return ret;
    }
    public String PrintStats(int[] SessionAggregationCount)
    {
        //if (SessionAggregationCount.Length < 2)
        //{ throw new System.Exception("Need multiple Aggs to calc some stats"); }
        string ret = this.FeatureName + "," + this.Samples.Count + ",";
        foreach (int SampleSize in SessionAggregationCount)
        {
            List<Sample> buff = new List<Sample>();
            List<Sample> aggregatedSamples = new List<Sample>();

            foreach (Sample sample in Samples)
            {
                buff.Add(sample);
                if (buff.Count == SampleSize)
                {
                    Sample Agg = new Sample(sample.FeatureName,
                        MeanHelper.FindMean((buff.ToArray().Select(x => x.MAD)).ToArray()),
                        MeanHelper.FindMean((buff.ToArray().Select(x => x.Mean)).ToArray()),
                        buff.First().StartTime,
                        buff.Last().EndTime,
                        buff.Last().SampleID / SampleSize,
                        buff.Last().SampleSize * SampleSize
                        );
                    aggregatedSamples.Add(Agg);
                    buff.Clear();
                }
            }
            // aggregatedSamples is a list of our bigger samples, reasdy for num crunching
            double[] Values = aggregatedSamples.ToArray().Select(x => x.Mean).ToArray();
            double min = Values.Min();
            double max = Values.Max();
            double range = max - min;
            double midpoint = (max + min) / 2;
            double variation = (100 / midpoint) * range;
            ret += midpoint.ToString("N1") + ",";
            ret += variation.ToString("N1") + ",";
        }
        ret.TrimEnd(',');
        ret += Environment.NewLine;
        return ret;
        // StatsDict contains our midpoint and range

    }
}

