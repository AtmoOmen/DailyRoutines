using System;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.Helpers;

public static unsafe class AgentHelper
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct EventObject
    {
        [FieldOffset(0)]
        public ulong Unknown0;

        [FieldOffset(8)]
        public ulong Unknown8;
    }

    public static EventObject* SendEvent(AgentId agentId, ulong eventKind, params object[] eventParams)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        return agent == null ? null : SendEvent(agent, eventKind, eventParams);
    }

    public static EventObject* SendEvent(AgentInterface* agentInterface, ulong eventKind, params object[] eventParams)
    {
        var eventObject = stackalloc EventObject[1];
        return SendEvent(agentInterface, eventObject, eventKind, eventParams);
    }

    public static EventObject* SendEvent(
        AgentInterface* agentInterface, EventObject* eventObject, ulong eventKind, params object[] eventParams)
    {
        var atkValues = CreateAtkValueArray(eventParams);
        var returnValues = new AtkValue(atkValues);
        if (atkValues == null) return eventObject;
        try
        {
            agentInterface->ReceiveEvent(&returnValues, atkValues, (uint)eventParams.Length, eventKind);
            return eventObject;
        }
        finally
        {
            for (var i = 0; i < eventParams.Length; i++)
                if (atkValues[i].Type == ValueType.String)
                    Marshal.FreeHGlobal(new nint(atkValues[i].String));
            Marshal.FreeHGlobal(new nint(atkValues));
        }
    }

    public static AtkValue* CreateAtkValueArray(params object[] values)
    {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
        if (atkValues == null) return null;
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                switch (v)
                {
                    case uint uintValue:
                        atkValues[i].Type = ValueType.UInt;
                        atkValues[i].UInt = uintValue;
                        break;
                    case int intValue:
                        atkValues[i].Type = ValueType.Int;
                        atkValues[i].Int = intValue;
                        break;
                    case float floatValue:
                        atkValues[i].Type = ValueType.Float;
                        atkValues[i].Float = floatValue;
                        break;
                    case bool boolValue:
                        atkValues[i].Type = ValueType.Bool;
                        atkValues[i].Byte = (byte)(boolValue ? 1 : 0);
                        break;
                    case string stringValue:
                        {
                            atkValues[i].Type = ValueType.String;
                            var stringBytes = Encoding.UTF8.GetBytes(stringValue);
                            var stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                            Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                            Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                            atkValues[i].String = (byte*)stringAlloc;
                            break;
                        }
                    default:
                        throw new ArgumentException($"Unable to convert type {v.GetType()} to AtkValue");
                }
            }
        }
        catch
        {
            return null;
        }

        return atkValues;
    }
}
