﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Analyzer.Parsing
{
    public class ParsedClassMonoCecil
    {
        private readonly TypeDefinition _typeObj;
        private readonly string? _name;
        private readonly List<MethodDefinition> _constructors;
        private readonly List<MethodDefinition> _methods;
        private readonly TypeDefinition? _parentClass;
        private readonly List<InterfaceImplementation> _interfaces;
        private readonly List<FieldDefinition> _fields;
        private readonly List<Type> _compositionList;
        private readonly List<Type> _aggregationList;
        private readonly List<Type> _usingList;
        private readonly List<Type> _inheritanceList;


        public ParsedClassMonoCecil(TypeDefinition type)
        {
            _typeObj = type;
            _name = type.FullName;
            _constructors = new List<MethodDefinition>();
            _methods = new List<MethodDefinition>();
            _interfaces = new List<InterfaceImplementation>();
            _fields = new List<FieldDefinition>();

            // type.Methods will include constructors of the class & will not give methods of parent class
            foreach (MethodDefinition method in type.Methods)
            {
                if(method.IsConstructor)
                {
                    _constructors.Add(method);
                }
                else
                {
                    _methods.Add(method);  
                }
            }

            if(type.BaseType != null)
            {
                _parentClass = type.BaseType.Resolve();
            }

            //_interfaces = type.Interfaces?.ToList().Except(_parentClass?.Interfaces?.ToList());
            if(type.HasInterfaces)
            {
                _interfaces = type.Interfaces.ToList();
                
                if(_parentClass?.Interfaces != null && _parentClass.Interfaces != null)
                {
                    _interfaces = type.Interfaces.Except(_parentClass.Interfaces).ToList();
                }


                HashSet<string> removableInterfaceNames = new();

                foreach (var i in _interfaces)
                {
                    foreach (var x in i.InterfaceType.Resolve().Interfaces)
                    {
                        removableInterfaceNames.Add(x.InterfaceType.FullName);
                    }
                }

                List<InterfaceImplementation> ifaceList = new();

                foreach (var iface in _interfaces)
                {
                    if (!removableInterfaceNames.Contains(iface.InterfaceType.FullName))
                    {
                        ifaceList.Add(iface);
                    }
                }

                _interfaces = ifaceList;
            }
            _fields = _typeObj.Fields.ToList();


            // Type Relationships
            // Using Class Relationship 
            // Cases considering: 1. if some method contains other class as parameter
            // TODO : Check for other cases of Using if exists
            _usingList = new List<Type>();
            _compositionList = new List<Type>();
            _aggregationList = new List<Type>();

            Dictionary<MethodDefinition, List<ParameterDefinition>> dict = GetFunctionParameters();
            foreach (KeyValuePair<MethodDefinition, List<ParameterDefinition>> pair in dict)
            {           
                foreach (ParameterDefinition argument in pair.Value)
                {

                    Type relatedClass = argument.GetType();

                    if (relatedClass.IsClass && relatedClass != _typeObj.GetType() && !relatedClass.IsGenericType)
                    {
                        //adding to using list
                        if (pair.Key.IsConstructor)
                        {
                            continue;                            
                        }
                        else
                        {
                            _usingList.Add(relatedClass);
                        }
                    }
                }
            }

            //Inheritance List
            _inheritanceList = new List<Type>();
            if(_parentClass != null) {
                _inheritanceList.Add(_parentClass.GetType());
            }
            else
            {
                foreach(var iface in _interfaces) {
                    _inheritanceList.Add(iface.GetType());
                }
            }


            // Aggregation List
            // check if new opcode is present in method body and get its type
            foreach (MethodDefinition method in _methods)
            {
                if (method.HasBody)
                {
                    foreach (var inst in method.Body.Instructions)
                    {
                        if(inst != null && inst.OpCode == OpCodes.Newobj)
                        {
                            var constructorReference = (MethodReference)inst.Operand;
                            var objectType = constructorReference.DeclaringType;
                            if(!objectType.IsGenericInstance) 
                                _aggregationList.Add(objectType.Resolve().GetType());
                        }
                    }
                }
            }
           

            //Composition
            foreach(MethodDefinition ctor in _constructors)
            {
                List<ParameterDefinition> parameterList = ctor.Parameters.ToList();
                if (ctor.HasBody)
                {
                    for(int i = 0; i < ctor.Body.Instructions.Count; i++)
                    {
                        var inst = ctor.Body.Instructions[i];
                        if(inst != null && inst.OpCode == OpCodes.Stfld) {
                            var fieldReference = (FieldReference)inst.Operand ;
                            var fieldType = fieldReference.FieldType;
                            var classType = fieldType.Resolve();
                            // Check if the field type is a reference type (not a value type)
                            if (!fieldType.IsValueType && classType.IsClass && !classType.IsGenericInstance)
                            {
                                _compositionList.Add(classType.Resolve().GetType());
                            }
                        } 
                    }
                }
                // TODO: When obj is taken as argument and assigned to a local variable-> using case
                // if between 2 classes between same method composition and using is used-> considering comp relation only?
                foreach(ParameterDefinition parameter in parameterList)
                {
                    var parameterType = parameter.Resolve().GetType();
                    if (parameterType.IsClass && !parameterType.IsGenericType && !_compositionList.Contains(parameterType))
                    {
                        _usingList.Add(parameterType);
                    }
                }


                //Get local variables of the constructor
                //And check
                //   TODO: case when new Obj is instantiated in constructor and assigned to local variable
                //var classType = localFieldType.Resolve();
                // Check if the field type is a reference type (not a value type)
                // if (!localFieldType.IsValueType && classType.IsClass && !classType.IsGenericInstance && classType != _typeObj)
                //{
                //   _aggregationList.Add(classType.GetType())
                //}
                //}

            }

        }

        public Dictionary<MethodDefinition, List<ParameterDefinition>> GetFunctionParameters()
        {
            Dictionary<MethodDefinition, List<ParameterDefinition>> dict = new();

            if (_methods != null)
            {
                foreach(MethodDefinition method in _methods)
                {
                    dict.Add(method, method.Parameters.ToList());
                }
            }

            return dict;
        }

        public TypeDefinition TypeObj
        {
            get { return _typeObj; }
        }

        public string Name
        {
            get { return _name; }
        }

        public List<MethodDefinition> Constructors
        {
            get { return _constructors; }
        }


    }
}
