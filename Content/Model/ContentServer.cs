﻿/******************************************************************************
 * Filename    = ContentServer.cs
 * 
 * Author      = Lekshmi
 *
 * Product     = Analyzer
 * 
 * Project     = Content
 *
 * Description = Class that represents a client for handling file uploads.
 *****************************************************************************/

using Networking.Communicator;
using Content.FileHandling;
using Content.Encoder;
using Analyzer;
using System.Drawing;
using System.Drawing.Imaging;

namespace Content.Model
{

    /// <summary>
    /// Server that handles upload of analysis files, and analysis of said files
    /// </summary>
    public class ContentServer
    {
        readonly ICommunicator _server;
        readonly string _hostSessionID;
        readonly IFileHandler _fileHandler;
        readonly IAnalyzer _analyzer;
        readonly AnalyzerResultSerializer _serializer;

        string? _sessionID;
        string? _fileEncoding;
        string? _resultEncoding;
        IDictionary<int, bool> _configuration;

        public Action<Dictionary<string, List<AnalyzerResult>>>? AnalyzerResultChanged;

        public Dictionary<string, List<AnalyzerResult>> analyzerResult { get; private set; }

        private readonly Dictionary<string, Dictionary<string, List<AnalyzerResult>>> _sessionAnalysisResultDict;
        private readonly object _sessionLock = new ();

        /// <summary>
        /// Initialise the content server, subscribe to networking server
        /// </summary>
        /// <param name="_server">Networking server</param>
        /// <param name="_analyzer">Analyzer</param>
        public ContentServer(ICommunicator _server, IAnalyzer _analyzer, string sessionID)
        {
            this._server = _server;
            _hostSessionID = sessionID;
            ServerRecieveHandler recieveHandler = new (this);
            this._server.Subscribe( recieveHandler , "Content-Files");

            _fileHandler = new FileHandler();

            this._analyzer = _analyzer;

            _serializer = new AnalyzerResultSerializer();

            analyzerResult = new();
            _sessionAnalysisResultDict = new();
        }

        /// <summary>
        /// Handles a recieved file by decoding and saving it, and then passing it to the analyser
        /// </summary>
        /// <param name="encodedFiles">The encoded file</param>
        /// <param name="clientID">Unique ID of client</param>
        public void HandleRecieve(string encodedFiles, string? clientID)
        {
            _fileEncoding = encodedFiles;
            // Save files to user session directory and collect sessionID
            string? recievedSessionID = _fileHandler.HandleRecieve(encodedFiles);
            if (recievedSessionID == null) 
            { 
                return; // FileHandler failed
            }

            // Analyse DLL files
            _analyzer.LoadDLLFileOfStudent(_fileHandler.GetFiles());

            // Save analysis results 
            lock (_sessionLock)
            {
                Dictionary<string , List<AnalyzerResult>> res = _analyzer.Run();
                Dictionary<string, List<AnalyzerResult>> customRes = _analyzer.RnuCustomAnalyzers();
                foreach (KeyValuePair<string, List<AnalyzerResult>> kvp in customRes)
                {
                    res[kvp.Key] = res[kvp.Key].Concat(kvp.Value).ToList();
                }

                _sessionAnalysisResultDict[recievedSessionID] = res;
                string serializedResults = _serializer.Serialize(res);
                _resultEncoding = serializedResults;
                _server.Send(serializedResults, "Content-Results", clientID);
                if (_sessionID == recievedSessionID)
                {
                    analyzerResult = res;
                    // Notification for viewModel
                    AnalyzerResultChanged?.Invoke(analyzerResult);
                }

                byte[] graph = _analyzer.GetRelationshipGraph(new());
                if (graph == null || graph.Length == 0)
                {
                    return;
                }

                using MemoryStream ms = new();
                Image image = Image.FromStream( ms );


                // Save the image as PNG
                image.Save( recievedSessionID + "/image.png" , ImageFormat.Png );
            }

        }
        /// <summary>
        /// Configures the analyzer with the specified configuration settings.
        /// </summary>
        /// <param name="configuration">The dictionary containing configuration settings.</param>
        public void Configure(IDictionary<int, bool> configuration)
        {
            _configuration = configuration;
            _analyzer.Configure(configuration);
        }

        /// <summary>
        /// Loads custom DLLs for additional analyzers.
        /// </summary>
        /// <param name="filePaths">The list of file paths for the custom DLLs.</param>
        public void LoadCustomDLLs(List<string> filePaths)
        {
            _analyzer.LoadDLLOfCustomAnalyzers(filePaths);
        }

        /// <summary>
        /// Sets the session ID and updates the associated analyzer results if available.
        /// </summary>
        /// <param name="sessionID">The session ID to be set.</param>
        public void SetSessionID(string? sessionID)
        {
            if (sessionID == null)
            {
                _sessionID = null;
                return;
            }

            _sessionID = sessionID;
            if (_sessionAnalysisResultDict.ContainsKey(sessionID)) 
            {
                // Use existing analyzer results if available for the given session ID.
                analyzerResult = _sessionAnalysisResultDict[sessionID];
            }
            else
            {
                // Create a new entry for the session ID if not present.
                analyzerResult = new();
                _sessionAnalysisResultDict[sessionID] = analyzerResult;
            }
            AnalyzerResultChanged?.Invoke(analyzerResult);
        }

        public void SendToCloud()
        {
            CloudHandler cloudHandler = new();
            Task.Run(() 
                => cloudHandler.PostSessionAsync(_hostSessionID, _configuration, _sessionAnalysisResultDict.Keys.ToList()));
            Task.Run(()
                => cloudHandler.PostSubmissionAsync(_hostSessionID, _fileEncoding));
            Task.Run(()
                => cloudHandler.PostSubmissionAsync(_hostSessionID, _resultEncoding));
        }

    }
}
