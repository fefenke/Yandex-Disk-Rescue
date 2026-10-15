using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiskRescue {
    // Append events without replacing the document the user is reading/selecting.
    public sealed class StableLog : TextBox {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
        [DllImport("user32.dll")] static extern int GetScrollPos(IntPtr hwnd,int bar);
        string[] previous=new string[0];
        readonly List<string> history=new List<string>();string query="";int filterMode;
        bool Matches(string line){
            if(line.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)<0)return false;
            if(filterMode==2)return line.EndsWith("  ·  success",StringComparison.Ordinal)||line.EndsWith("  ·  local",StringComparison.Ordinal);
            return filterMode!=1||new[]{"error","failed","timeout","ошиб","сбой","таймаут","HTTP"}.Any(x=>line.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0);
        }
        public void SetFilter(string text,int mode){query=text;filterMode=mode;Text=String.Join(Environment.NewLine,history.Where(Matches));Select(0,0);}

        public void UpdateEvents(List<string> events){
            string[] next=events.ToArray();if(previous.SequenceEqual(next))return;
            int overlap=0;
            for(int n=Math.Min(previous.Length,next.Length);n>0;n--){if(previous.Skip(previous.Length-n).SequenceEqual(next.Take(n))){overlap=n;break;}}
            previous=next;if(overlap==next.Length)return;
            string[] incoming=next.Skip(overlap).ToArray();history.AddRange(incoming);string addition=String.Join(Environment.NewLine,incoming.Where(Matches));if(addition.Length==0)return;
            int start=SelectionStart,length=SelectionLength;
            int top=SendMessage(Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32();
            int horizontal=GetScrollPos(Handle,0);
            SendMessage(Handle,0xB,IntPtr.Zero,IntPtr.Zero);
            try{
                AppendText((TextLength>0?Environment.NewLine:"")+addition);
                Select(start,length);int now=SendMessage(Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32();SendMessage(Handle,0xB6,IntPtr.Zero,new IntPtr(top-now));SendMessage(Handle,0x114,new IntPtr(4|(horizontal<<16)),IntPtr.Zero);
            }finally{SendMessage(Handle,0xB,new IntPtr(1),IntPtr.Zero);Invalidate();}
        }
    }
}
