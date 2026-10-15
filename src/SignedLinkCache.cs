using System;
using System.Collections.Generic;

namespace DiskRescue {
    // Signed URLs stay in memory only; they must never enter history or diagnostics.
    internal sealed class SignedLinkCache {
        internal sealed class Lease {
            public readonly string Key,Identity;public readonly Uri Link;
            internal bool Confirmed,Invalid;
            internal Lease(string key,string identity,Uri link){Key=key;Identity=identity;Link=link;}
        }
        readonly object sync=new object();
        readonly Dictionary<string,Lease> entries=new Dictionary<string,Lease>(StringComparer.Ordinal);
        public Lease Get(string key,string identity){lock(sync){
            Lease lease;if(!entries.TryGetValue(key,out lease))return null;
            if(lease.Identity!=identity){lease.Invalid=true;entries.Remove(key);return null;}
            return lease.Confirmed&&!lease.Invalid?lease:null;
        }}
        public Lease Offer(string key,string identity,Uri link){lock(sync){
            Lease old;if(entries.TryGetValue(key,out old))old.Invalid=true;
            Lease lease=new Lease(key,identity,link);entries[key]=lease;return lease;
        }}
        public void Confirm(Lease lease){if(lease==null)return;lock(sync){if(!lease.Invalid)lease.Confirmed=true;}}
        public void Invalidate(Lease lease){if(lease==null)return;lock(sync){
            lease.Invalid=true;Lease current;if(entries.TryGetValue(lease.Key,out current)&&Object.ReferenceEquals(current,lease))entries.Remove(lease.Key);
        }}
        public void Discard(string key){lock(sync){Lease lease;if(entries.TryGetValue(key,out lease)){lease.Invalid=true;entries.Remove(key);}}}
    }
}
