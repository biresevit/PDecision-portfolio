using System.Reflection;
using DBLib;
using DecisionMaking;
using DecisionMaking.DecisionMaking;

// A failed refresh after changing customer must never expose the previous customer's cache.
var type=typeof(DbStrategySettings);
var cacheType=type.GetNestedType("Snapshot",BindingFlags.NonPublic)!;
var cache=type.GetField("snapshot",BindingFlags.Static|BindingFlags.NonPublic)!;
void Publish(int customer,bool help){
 var settings=new Dictionary<EnumCasino,CasinoSettings>{[EnumCasino.Evenbet]=new(){UseHelp=help}};
 cache.SetValue(null,Activator.CreateInstance(cacheType,customer,settings));
}
Singleton.RegisteredCustomerId=100;
Publish(100,true);
if(DbStrategySettings.Get(EnumCasino.Evenbet)?.UseHelp!=true)throw new Exception("Customer A settings missing");
Singleton.RegisteredCustomerId=200;
if(DbStrategySettings.Get(EnumCasino.Evenbet)!=null)throw new Exception("Customer A cache leaked to customer B");
Publish(200,false);
if(DbStrategySettings.Get(EnumCasino.Evenbet)?.UseHelp!=false)throw new Exception("Customer B settings missing");
Singleton.RegisteredCustomerId=100;
if(DbStrategySettings.Get(EnumCasino.Evenbet)!=null)throw new Exception("Customer B cache leaked to customer A");
Singleton.RegisteredCustomerId=null;
if(DbStrategySettings.Get(EnumCasino.Evenbet)!=null)throw new Exception("Customer cache leaked into an unregistered tool");
Console.WriteLine("Customer strategy cache isolation passed.");
