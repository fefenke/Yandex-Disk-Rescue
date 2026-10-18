using System;
using System.Net.Http;

namespace DiskRescue {
    public sealed class Engine {
        public volatile Snapshot Latest = new Snapshot();
        readonly object gate = new object();
        readonly ApiPacer apiPacer;
        readonly SignedLinkCache linkCache;
        readonly HttpMessageHandler apiHandler, fileHandler;
        ApiEngine apiEngine;
        bool stopRequested;
        public string DataFolder { get; private set; }
        public Engine(string dataFolder):this(dataFolder,new ApiPacer(),new SignedLinkCache()){}
        internal Engine(string dataFolder,ApiPacer pacer):this(dataFolder,pacer,new SignedLinkCache()){}
        internal Engine(string dataFolder,ApiPacer pacer,SignedLinkCache links):this(dataFolder,pacer,links,null,null){}
        internal Engine(string dataFolder,ApiPacer pacer,SignedLinkCache links,HttpMessageHandler apiHandler,HttpMessageHandler fileHandler){
            DataFolder=dataFolder;apiPacer=pacer;linkCache=links;this.apiHandler=apiHandler;this.fileHandler=fileHandler;
        }
        public void Stop(){
            lock(gate){stopRequested=true;if(apiEngine!=null)apiEngine.Stop();}
        }
        public void Run(Settings settings,RunMode mode){
            ApiEngine current;
            lock(gate){
                if(apiEngine!=null)throw new InvalidOperationException("A pass is already running.");
                current=new ApiEngine(DataFolder,s=>Latest=s,apiHandler,fileHandler,apiPacer,linkCache);
                apiEngine=current;if(stopRequested)current.Stop();
            }
            try{current.Run(settings,mode);}
            finally{lock(gate){apiEngine=null;stopRequested=false;}}
        }
    }
}
