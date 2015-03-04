﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Collections;
using System.Data.SqlClient;

namespace SQLLog
{
    class Program
    {
        static void Main(string[] args)
        {
            //Ignore bad certificates - like the self signed one AGS generates itself.
            ServicePointManager.ServerCertificateValidationCallback += delegate { return true; };

            string home = Directory.GetCurrentDirectory();

            var MyIni = new IniFile();

            // var MyIni = new IniFile();
            string slastrun;

            //if (slastrun == "") slastrun = "0";

            double dthisrun = (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
            dthisrun = Math.Truncate(dthisrun);

            string sthisrun = dthisrun.ToString();
            sthisrun = sthisrun.Substring(0, sthisrun.Length);

            string ExeFriendlyName = System.AppDomain.CurrentDomain.FriendlyName;
            string[] ExeNameBits = ExeFriendlyName.Split('.');
            string ExeName = ExeNameBits[0];

            bool debug = false;
            bool proxy = false;
            bool cleanlogs = false;
            bool incremental = false;
            bool usedbtime = false;

            string TLogsURL = "";
            string TTokenURL = "";
            string TUser = "";
            string TPassword = "";

            string Tdbuser = "";
            string Tdbpassword = "";
            string Tdbname = "";
            string Tdbserver = "";
            string Tdbschema = "dbo";
            string Tpagesize = "1000";
            string Tmsglvl = "FINE";

            string Tfilter = "";

            string Tsrid = "null";

            int c = args.GetUpperBound(0);

            // Create list of valid message levels

            string[] MsgLvls = new string[6];
            MsgLvls[0] = "SEVERE";
            MsgLvls[1] = "WARNING";
            MsgLvls[2] = "INFO";
            MsgLvls[3] = "FINE";
            MsgLvls[4] = "VERBOSE";
            MsgLvls[5] = "DEBUG";



            // Loop through arguments
            for (int n = 0; n < c; n++)
            {
                string thisKey = args[n].ToLower();
                string thisVal = args[n + 1].TrimEnd().TrimStart();

                // eval the key
                switch (thisKey)
                {
                    case "-logsurl":
                        TLogsURL = thisVal;
                        break;                    
                    case "-debug":
                        string dbg = thisVal;
                        if (dbg.ToUpper() == "Y") debug = true;
                        break;
                    case "-proxy":
                        string prx = thisVal;
                        if (prx.ToUpper() == "Y") proxy = true;
                        break;
                    case "-cleanlogs":
                        string clg = thisVal;
                        if (clg.ToUpper() == "Y") cleanlogs = true;
                        break;
                    case "-user":
                        TUser = thisVal;
                        break;
                    case "-password":
                        TPassword = thisVal;
                        break;
                    case "-tokenurl":
                        TTokenURL = thisVal;
                        break;
                    case "-dbuser":
                        Tdbuser = thisVal;
                        break;
                    case "-dbpassword":
                        Tdbpassword = thisVal;
                        break;
                    case "-dbname":
                        Tdbname = thisVal;
                        break;
                    case "-dbserver":
                        Tdbserver = thisVal;
                        break;
                    case "-dbschema":
                        Tdbschema = thisVal;
                        break;
                    case "-filter":
                        Tfilter = thisVal;
                        break;
                    case "-srid":
                        Tsrid = thisVal;
                        break;
                    case "-incremental":
                        string inc = thisVal;
                        if (inc.ToUpper() == "Y") incremental = true;
                        break;
                    case "-pagesize":
                        Tpagesize = thisVal;
                        break;
                    case "-usedbtime":
                        string dbt = thisVal;
                        if (dbt.ToUpper() == "Y") usedbtime = true;
                        break;
                    case "-messagelevel":
                        if (MsgLvls.Contains<string>(thisVal.ToUpper()) == true) Tmsglvl = thisVal.ToUpper();
                        break;

                    default:
                        break;
                }
            }

            if (usedbtime == true)
            {
                slastrun = GetLastDatabaseTime(Tdbuser,Tdbserver,Tdbname,Tdbpassword,debug);
            }
            else
            {
                slastrun = MyIni.Read("LastRun");
            };


            if (debug == true) Console.WriteLine("Last run " + slastrun);
            if (debug == true) Console.WriteLine("This run " + sthisrun);

            if (TLogsURL == "") return;

            string[] temp_ms = TLogsURL.Split('/');

            string Token = "";

            string logsurl = TLogsURL;

            WebClient client = new WebClient();

            if (proxy == true)
            {
                client.Proxy = WebRequest.DefaultWebProxy;
                client.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            if (TTokenURL != "" && TPassword != "" && TUser != "")
            {
                Token = GetToken(TTokenURL, TUser, TPassword);
                if (Token.Contains("Token Error:"))
                {
                    Console.WriteLine(Token);
                    Environment.Exit(-1);
                }
            }

            string json = "";

            try
            {
                if (Token != "")
                {
                    json = client.DownloadString(new Uri(logsurl + "?f=json&token=" + Token));
                }
                else
                {
                    json = client.DownloadString(new Uri(logsurl + "?f=json"));
                }

            }
            catch (WebException webEx)
            {
                Console.WriteLine(webEx.Message);
                Environment.Exit(-1);
            }                

            //Some Esri error
            if (json.ToLower().Contains("error"))
            {
                Console.WriteLine(json);
                Environment.Exit(-1);
            }

            if (debug == true) Console.WriteLine(logsurl);
            if (debug == true) Console.WriteLine("");

            Stopwatch sw = Stopwatch.StartNew();

            bool hasMore = true;
            double prevEndTime = 0;
            int requests = 0;

            while (hasMore == true)
            {
                string imgurl = logsurl + "/query?";

                if (debug == true) Console.WriteLine("Previous End Time = " + prevEndTime.ToString());

                //If there is no last run and this is the first request then use sinceLastStart 
                if (slastrun == "" && prevEndTime == 0)
                {
                    if (debug == true) Console.WriteLine("Using sinceLastStart=true (must be first run)");
                    imgurl = imgurl + "&sinceLastStart=true";
                }
                else
                {
                    //If we have a previous end time we must be in a hasMore loop so set the startTime
                    if (prevEndTime > 0)
                    {
                        if (debug == true) Console.WriteLine("prevEndTime > 0 (" + prevEndTime.ToString() + ")");
                        //startTime = most recent time to query
                        imgurl = imgurl + "&startTime=" + prevEndTime.ToString();
                    }

                    //If we have a last run then use that to limit the results
                    if (slastrun != "")
                    {
                        if (debug == true) Console.WriteLine("Using endTime=" + slastrun);
                        imgurl = imgurl + "&endTime=" + slastrun;
                    }
                    else
                    {
                        if (debug == true) Console.WriteLine("Using sinceLastStart=true (must be first run but prevEndTime > 0)");
                        imgurl = imgurl + "&sinceLastStart=true";
                    }
                }
                // Include every possible message.  User can restrict logging level from ArcGIS for Server Manager.  
                imgurl = imgurl + "&level=" + Tmsglvl; 
                imgurl = imgurl + "&filterType=json";
                imgurl = imgurl + "&filter=" + Tfilter; //"{\"server\": \"*\", \"services\": \"*\", \"machines\":\"*\" }";

                imgurl = imgurl + "&pageSize=" + Tpagesize;
                imgurl = imgurl + "&f=pjson";                

                if (Token != "")
                {
                    //Check token is valid, if not get a new one.
                    if (CheckTokenValid(logsurl, Token, proxy, debug) == false)
                    {
                        if (debug == true) Console.WriteLine("Token expired, get a new one.");
                        if (debug == true) Console.WriteLine("Old token=" + Token);
                        Token = GetToken(TTokenURL, TUser, TPassword);
                        if (debug == true) Console.WriteLine("New token=" + Token);
                    }

                    imgurl = imgurl + "&token=" + Token;
                }


                string response = "";

                try
                {
                    requests++;
                    response = client.DownloadString(new Uri(imgurl));
                    if (debug == true) Console.WriteLine(imgurl + " OK");
                    int result = ProcessResponse(response, Tdbuser, Tdbpassword, Tdbname, Tdbserver, Tdbschema, debug, Tsrid);

                    if (result < 0)
                    {
                        Console.WriteLine("Error inserting records");
                        System.Environment.Exit(1);
                    }

                    Console.WriteLine("Inserted " + result.ToString() + " records");

                    Hashtable root;
                    root = (Hashtable)Procurios.Public.JSON.JsonDecode(response);
                    hasMore = (bool)root["hasMore"];
                    if (hasMore == true) prevEndTime = (double)root["endTime"];
                }
                catch (WebException webEx)
                {
                    if (debug == true) Console.WriteLine(imgurl + " " + webEx.Message);
                    hasMore = false;
                }
            }

            sw.Stop();

            double seconds = sw.ElapsedMilliseconds;

            if (debug == true) Console.WriteLine("Made " + requests.ToString() + " successful requests in " + (seconds/1000).ToString() + " seconds");

            if (debug == true) Console.WriteLine("Cleaning out logs (" + cleanlogs.ToString() + ")");

            if (cleanlogs == true)
            {
                string cleanurl = logsurl + "/clean?";

                cleanurl = cleanurl + "f=json";

                if (Token != "") cleanurl = cleanurl + "&token=" + Token;

                try
                {
                    client.UploadString(new Uri(cleanurl), "");
                    if (debug == true) Console.WriteLine(cleanurl + " OK");
                }
                catch (WebException webEx)
                {
                    if (debug == true) Console.WriteLine(cleanurl + " " + webEx.Message);
                    System.Environment.Exit(1);
                }
            }

            MyIni.Write("LastRun", sthisrun);

            Console.WriteLine("Done!");
        }

        public static bool CheckTokenValid(string logsurl, string Token, bool proxy, bool debug)
        {
            WebClient client = new WebClient();

            if (proxy == true)
            {
                client.Proxy = WebRequest.DefaultWebProxy;
                client.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }

            string json = "";

            try
            {
                json = client.DownloadString(new Uri(logsurl + "?f=json&token=" + Token));
            }
            catch (WebException webEx)
            {
                if (debug == true) Console.WriteLine(webEx.Message);
                return false;
            }

            //Token expired
            if (json.ToLower().Contains("token expired"))
            {
                if (debug == true) Console.WriteLine(json);
                return false;
            }

            //Some other Esri error
            if (json.ToLower().Contains("error"))
            {
                if (debug == true) Console.WriteLine(json);
                return false;
            }

            return true;
        }

        public static int ProcessResponse(string response, string dbuser, string dbpassword, string dbname, string dbserver, string dbschema, bool debug, string srid)
        {
            Hashtable root;
            ArrayList LogRecords;
            long LogRecordsCount = 0;

            root = (Hashtable)Procurios.Public.JSON.JsonDecode(response);

            LogRecords = (ArrayList)root["logMessages"];

            if (LogRecords == null)
            {
                if (debug == true) Console.WriteLine("LogRecords is null");
                return -1;
            }

            LogRecordsCount = LogRecords.Count;

            if (debug == true) Console.WriteLine("Retrieved " + LogRecordsCount.ToString() + " records");

            SqlConnection myConnection;

            if (dbuser != "")
            {
                myConnection = new SqlConnection("Server=" + dbserver + "; Database=" + dbname + "; User ID=" + dbuser + "; Password=" + dbpassword);
            }
            else
            {
                myConnection = new SqlConnection("Server=" + dbserver + "; Database=" + dbname + ";Integrated Security=true");
            }

            try
            {
                myConnection.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not open up SQL Connection");
                if (debug == true) Console.WriteLine(e.ToString());
                return -1;
            }

            int n;

            for (n = 0; n < LogRecordsCount; n++)
            {
                Hashtable LogRecord = (Hashtable)LogRecords[n];

                string type = (string)LogRecord["type"];
                string message = (string)LogRecord["message"];
                double dtime = (double)LogRecord["time"];
                string source = (string)LogRecord["source"];
                string machine = (string)LogRecord["machine"];
                string user = (string)LogRecord["user"];
                double code = (double)LogRecord["code"];
                string elapsed = (string)LogRecord["elapsed"];
                string process = (string)LogRecord["process"];
                string thread = (string)LogRecord["thread"];
                string methodName = (string)LogRecord["methodName"];

                string scale = "NULL";

                string size_x = "NULL";
                string size_y = "NULL";

                string minx = "NULL";
                string miny = "NULL";
                string maxx = "NULL";
                string maxy = "NULL";

                string Shape = "NULL";

                DateTime dttime = UnixTimeStampToDateTime2(dtime);

                dttime = dttime.ToLocalTime();

                long lcode = (long)code;

                if (message.Length > 4000) message = message.Substring(0, 4000);
                if (methodName.Length > 50) methodName = methodName.Substring(0, 50);

                message = message.Replace("'", "''");

                if (message.Contains("Extent:"))
                {
                    string[] vals = message.Split(';');

                    string tmp_extent_all = vals[0];
                    string[] tmp_extent = vals[0].Split(':');
                    string[] tmp_size = vals[1].Split(':');
                    string[] tmp_scale = vals[2].Split(':');

                    string[] tmp_sizes = tmp_size[1].Split(',');
                    string[] tmp_extents = tmp_extent[1].Split(',');

                    scale = tmp_scale[1];

                    size_x = tmp_sizes[0];
                    size_y = tmp_sizes[1];

                    if (tmp_extent_all.ToUpper().Contains("NAN") == false)
                    {
                        minx = tmp_extents[0];
                        miny = tmp_extents[1];
                        maxx = tmp_extents[2];
                        maxy = tmp_extents[3];

                        Shape = "'POLYGON((" + minx + " " + miny + "," + minx + " " + maxy + "," + maxx + " " + maxy + "," + maxx + " " + miny + "," + minx + " " + miny + "))'";

                        Shape = "geometry::STPolyFromText(" + Shape + ", " + srid + ")";
                    }
                }

                string sql = "";

                sql = sql + "INSERT INTO [" + dbname + "].[" + dbschema + "].[RawLogs]";
                sql = sql + "([type]";
                sql = sql + ",[message]";
                sql = sql + ",[time]";
                sql = sql + ",[source]";
                sql = sql + ",[machine]";
                sql = sql + ",[username]";
                sql = sql + ",[code]";
                sql = sql + ",[elapsed]";
                sql = sql + ",[process]";
                sql = sql + ",[thread]";
                sql = sql + ",[methodname]";
                sql = sql + ",[mapsize_x]";
                sql = sql + ",[mapsize_y]";
                sql = sql + ",[mapscale]";
                sql = sql + ",[mapextent_minx]";
                sql = sql + ",[mapextent_miny]";
                sql = sql + ",[mapextent_maxx]";
                sql = sql + ",[mapextent_maxy]";
                sql = sql + ",[Shape])";
                sql = sql + "VALUES";
                sql = sql + "(";
                sql = sql + "'" + type + "',";
                sql = sql + "'" + message + "',";
                sql = sql + "'" + dttime.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "',";
                sql = sql + "'" + source + "',";
                sql = sql + "'" + machine + "',";
                sql = sql + "'" + user + "',";
                sql = sql + code + ",";
                sql = sql + "'" + elapsed + "',";
                sql = sql + "'" + process + "',";
                sql = sql + "'" + thread + "',";
                sql = sql + "'" + methodName + "',";
                sql = sql + "" + size_x + ",";
                sql = sql + "" + size_y + ",";
                sql = sql + "" + scale + ",";
                sql = sql + "" + minx + ",";
                sql = sql + "" + miny + ",";
                sql = sql + "" + maxx + ",";
                sql = sql + "" + maxy + ",";
                sql = sql + Shape;
                sql = sql + ")";

                SqlCommand myUserCommand = new SqlCommand(sql, myConnection);

                try
                {
                    myUserCommand.ExecuteNonQuery();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not execute SQL statement");
                    Console.WriteLine(e.ToString());
                    Console.WriteLine("Last SQL statement");
                    Console.WriteLine(sql);

                    myConnection.Close();

                    return -1;
                }

                if (debug == true) Console.WriteLine("Inserted record " + (n+1).ToString());
            }

            myConnection.Close();

            return n;
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime - new DateTime(1970, 1, 1,0,0,0,0).ToLocalTime()).TotalSeconds;
            
        }

        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        static readonly double MaxUnixSeconds = (DateTime.MaxValue - UnixEpoch).TotalSeconds;

        public static DateTime UnixTimeStampToDateTime2(double unixTimeStamp)
        {
            return unixTimeStamp > MaxUnixSeconds
               ? UnixEpoch.AddMilliseconds(unixTimeStamp)
               : UnixEpoch.AddSeconds(unixTimeStamp);
        }

        public static double DateTime2ToUnixTimeStamp(DateTime dt2)
        {

            return (dt2.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
               
        }

        public static string GetToken(string tokenurl, string username, string password)
        {
            string url = tokenurl + "?request=getToken&username=" + username + "&password=" + password + "&expiration=60";

            System.Net.WebRequest request = System.Net.WebRequest.Create(url);

            string myToken = "";

            try
            {
                System.Net.WebResponse response = request.GetResponse();
                System.IO.Stream responseStream = response.GetResponseStream();
                System.IO.StreamReader readStream = new System.IO.StreamReader(responseStream);

                myToken = readStream.ReadToEnd();
            }

            catch (WebException we)
            {
                myToken = "Token Error: " + we.Message;
            }

            return myToken;
        }

        public static string GetLastDatabaseTime (string dbuser, string dbserver, string dbname, string dbpassword, bool debug)
        {

            SqlConnection myConnection;

            if (dbuser != "")
            {
                myConnection = new SqlConnection("Server=" + dbserver + "; Database=" + dbname + "; User ID=" + dbuser + "; Password=" + dbpassword);
            }
            else
            {
                myConnection = new SqlConnection("Server=" + dbserver + "; Database=" + dbname + ";Integrated Security=true");
            }

            try
            {
                myConnection.Open();
            }
            catch (Exception e)
            {
               
                Console.WriteLine("Could not open up SQL Connection");
                if (debug == true) Console.WriteLine(e.ToString());
                return null;
            }

            string sql = "SELECT MAX([time]) as MaxTime FROM RawLogs";
            SqlCommand cmd = new SqlCommand(sql,myConnection);
           
            DateTime dt = (DateTime) cmd.ExecuteScalar();
            myConnection.Close();

            double totalms = DateTime2ToUnixTimeStamp(dt);

            return Math.Truncate(totalms).ToString().Substring(0, Math.Truncate(totalms).ToString().Length);
          
        }
    }
}
