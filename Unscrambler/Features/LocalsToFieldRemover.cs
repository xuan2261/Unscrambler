﻿using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

namespace Unscrambler.Features
{
    public class LocalsToFieldRemover : IFeature
    {
        private int _count;
        private static readonly HashSet<FieldDefinition> FieldsInModule = new HashSet<FieldDefinition>();

        private static readonly Dictionary<FieldDefinition, CilLocalVariable> CreatedLocals =
            new Dictionary<FieldDefinition, CilLocalVariable>();

        public void Process( TypeDefinition type )
        {
            // On first execution, grab fields from global constructor
            if ( FieldsInModule.Count == 0 )
            {
                var globalType = type.Module.GetOrCreateModuleType();

                foreach ( var field in globalType.Fields )
                {
                    // This assumes that the fields have no default value (which the forks Ive checked didnt have)
                    if ( !field.IsStatic || field.IsPrivate || field.HasDefault )
                        continue;
                    FieldsInModule.Add( field );
                }
            }

            foreach ( var method in type.Methods.Where( m => m.CilMethodBody != null ) )
            {
                var instr = method.CilMethodBody.Instructions;
                for ( int i = 0; i < instr.Count; i++ )
                {
                    if ( instr[i].OpCode.OperandType != CilOperandType.InlineField )
                        continue;
                    if ( !( instr[i].Operand is FieldDefinition field ) || !FieldsInModule.Contains( field ) )
                        continue;

                    // Check if dictionary already contains a local for the field, if not create a new one and add it to the dictionary
                    if ( !CreatedLocals.TryGetValue( field, out var createdLocal ) )
                    {
                        createdLocal = new CilLocalVariable( field.Signature.FieldType );
                        method.CilMethodBody.LocalVariables.Add( createdLocal );
                        CreatedLocals.Add( field, createdLocal );
                    }

                    // Get local from dictionary
                    instr[i].OpCode = GetOpCode( instr[i].OpCode.Code );
                    instr[i].Operand = createdLocal;
                    _count++;
                }

                instr.OptimizeMacros();
            }
        }

        public void PostProcess( ModuleDefinition module )
        {
            var globalType = module.GetModuleType();

            foreach ( var item in CreatedLocals )
            {
                globalType.Fields.Remove( item.Key );
            }
        }

        public IEnumerable<Summary> GetSummary()
        {
            if ( _count > 0 )
                yield return new Summary( $"Removed {_count} Local to Field implementations", Logger.LogType.Success );
        }

        private static CilOpCode GetOpCode( CilCode opcode )
        {
            switch ( opcode )
            {
                case CilCode.Stsfld:
                    return CilOpCodes.Stloc;
                case CilCode.Ldsfld:
                    return CilOpCodes.Ldloc;
                case CilCode.Ldsflda:
                    return CilOpCodes.Ldloca;
            }

            return CilOpCodes.Nop;
        }
    }
}