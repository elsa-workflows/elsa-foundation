using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;

// Short, local, single-connection relay. Direction counter follows landed EngineRun.Wire.
// Frames decoded only inside the armed, post-auth, aligned measurement interval.
// No payload is retained other than server CommandComplete tags (e.g. COMMIT/SELECT 1).
sealed class Wire : IDisposable {
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly TcpClient upstream = new(){NoDelay=true};
    TcpClient? client;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    int serverSpoke=1; long trips;
    public long Trips => Interlocked.Read(ref trips);
    public bool Armed; public bool Hold; int heldAfter;
    public void ArmHold(){heldAfter=back.Count;Volatile.Write(ref Hold,true);}
    public readonly TaskCompletionSource<bool> Held=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource<bool> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Frames front=new(false), back=new(true);
    public string Trace => $"frontend={front} backend={back}";
    public Wire(int port) {listener.Start(); _=Relay(port);}
    async Task Relay(int port){
        try {
            client=await listener.AcceptTcpClientAsync();client.NoDelay=true;
            await upstream.ConnectAsync(IPAddress.Loopback,port);
            async Task Pump(NetworkStream from,NetworkStream to,bool outgoing){
                byte[] b=new byte[65536]; try{
                    int n; while((n=await from.ReadAsync(b))>0){
                        if(outgoing){if(Interlocked.Exchange(ref serverSpoke,0)==1) Interlocked.Increment(ref trips);}
                        else Interlocked.Exchange(ref serverSpoke,1);
                        if(Volatile.Read(ref Armed)) (outgoing?front:back).Add(b,n);
                        if(!outgoing && Volatile.Read(ref Hold)){
                            // Swallow every byte. Require a NEW ReadyForQuery with idle transaction status.
                            // This also observes the implicit commit of a single atomic SQL statement.
                            if(back.IdleSince(heldAfter)){Held.TrySetResult(true);await Release.Task;break;}
                            continue;
                        }
                        await to.WriteAsync(b.AsMemory(0,n));
                    }
                }catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
                finally{client?.Close();upstream.Close();}
            }
            await Task.WhenAll(Pump(client.GetStream(),upstream.GetStream(),true),Pump(upstream.GetStream(),client.GetStream(),false));
        }catch(ObjectDisposedException){}catch(SocketException){}
    }
    public void Dispose(){Release.TrySetResult(false);client?.Close();upstream.Close();listener.Stop();}
    sealed class Frames(bool server){
        readonly List<byte> pending=[]; readonly List<string> trace=[];
        public void Add(byte[] buf,int count){lock(pending){
            pending.AddRange(buf.Take(count));
            while(pending.Count>=5){
                int size=BinaryPrimitives.ReadInt32BigEndian(pending.GetRange(1,4).ToArray());
                if(size<4||size>1024*1024)throw new InvalidDataException("unframed trace");
                if(pending.Count<1+size)return;
                char type=(char)pending[0];string token=type.ToString();
                if(server && type=='C') token+=":"+System.Text.Encoding.ASCII.GetString(pending.GetRange(5,size-5).ToArray());
                if(server && type=='Z') token+=":"+(char)pending[5];
                trace.Add(token);pending.RemoveRange(0,1+size);
            }
        }}
        public int Count {get{lock(pending)return trace.Count;}}
        public bool IdleSince(int prior){lock(pending)return trace.Count>prior && trace[^1]=="Z:I";}
        public override string ToString(){lock(pending)return string.Join(',',trace);}
    }
}
