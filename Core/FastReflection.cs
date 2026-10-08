using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RadioWars.Core
{
    public static class FastReflection
    {
        public static Func<TTarget, TField> CreateFieldGetter<TTarget, TField>(string fieldName)
        {
            try
            {
                FieldInfo fi = AccessTools.Field(typeof(TTarget), fieldName);
                if (fi == null) return null;
                DynamicMethod dm = new DynamicMethod("Get_" + typeof(TTarget).Name + "_" + fieldName,
                    typeof(TField), new Type[] { typeof(TTarget) }, typeof(TTarget), true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fi);
                il.Emit(OpCodes.Ret);
                return (Func<TTarget, TField>)dm.CreateDelegate(typeof(Func<TTarget, TField>));
            }
            catch
            {
                return null;
            }
        }

        public static Action<TTarget, TField> CreateFieldSetter<TTarget, TField>(string fieldName)
        {
            try
            {
                FieldInfo fi = AccessTools.Field(typeof(TTarget), fieldName);
                if (fi == null) return null;
                DynamicMethod dm = new DynamicMethod("Set_" + typeof(TTarget).Name + "_" + fieldName,
                    typeof(void), new Type[] { typeof(TTarget), typeof(TField) }, typeof(TTarget), true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, fi);
                il.Emit(OpCodes.Ret);
                return (Action<TTarget, TField>)dm.CreateDelegate(typeof(Action<TTarget, TField>));
            }
            catch
            {
                return null;
            }
        }
    }
}
