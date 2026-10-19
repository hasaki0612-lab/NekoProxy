using System.Text;
namespace NekoProxy.Desktop.ViewModels;

// Bounded line terminal. Handles fragmented CSI/OSC, CR, backspace and line erasure.
// It deliberately does not implement full-screen cursor addressing or a TUI emulator.
public sealed class TerminalBuffer
{
    private readonly StringBuilder _text=new();
    private string _pending="";
    private int _lineStart,_cursor;
    public string Text=>_text.ToString();
    public void Clear(){_text.Clear();_pending="";_lineStart=_cursor=0;}
    public void Append(string value)
    {
        value=_pending+value;_pending="";
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(c=='\u001b')
            {
                var start=i;
                if(++i>=value.Length){_pending=value[start..];break;}
                if(value[i]=='[')
                {
                    var from=++i;while(i<value.Length&&(value[i]<'@'||value[i]>'~'))i++;
                    if(i==value.Length){_pending=value[start..];break;}
                    int.TryParse(value[from..i],out var n);Csi(value[i],n);
                }
                else if(value[i]==']')
                {
                    while(++i<value.Length&&value[i]!='\a'&&!(value[i]=='\u001b'&&i+1<value.Length&&value[i+1]=='\\')){}
                    if(i==value.Length){_pending=value[start..];break;}
                    if(value[i]=='\u001b')i++;
                }
                continue;
            }
            if(c=='\r'){_cursor=_lineStart;continue;}
            if(c=='\b'){_cursor=Math.Max(_lineStart,_cursor-1);continue;}
            if(c=='\n'){_text.Append('\n');_lineStart=_cursor=_text.Length;continue;}
            if(c=='\t'){var count=8-(_cursor-_lineStart)%8;for(var j=0;j<count;j++)Put(' ');continue;}
            if(!char.IsControl(c))Put(c);
        }
        if(_text.Length>64000){var count=_text.Length-64000;_text.Remove(0,count);_lineStart=Math.Max(0,_lineStart-count);_cursor=Math.Max(0,_cursor-count);}
        if(_pending.Length>1024)_pending="";
    }
    private void Put(char c){if(_cursor<_text.Length)_text[_cursor]=c;else _text.Append(c);_cursor++;}
    private void Csi(char operation,int n)
    {
        switch(operation)
        {
            case 'K':if(n==2){_text.Length=_lineStart;_cursor=_lineStart;}else if(n==1){for(var i=_lineStart;i<Math.Min(_cursor+1,_text.Length);i++)_text[i]=' ';}else _text.Length=_cursor;break;
            case 'J':if(n==2)Clear();break;
            case 'G':_cursor=Math.Clamp(_lineStart+Math.Max(1,n)-1,_lineStart,_text.Length);break;
            case 'C':_cursor=Math.Min(_text.Length,_cursor+Math.Max(1,n));break;
            case 'D':_cursor=Math.Max(_lineStart,_cursor-Math.Max(1,n));break;
        }
    }
}
