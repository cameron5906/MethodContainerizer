using MethodContainerizer.Extensions;
using SDILReader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MethodContainerizer
{
    /// <summary>
    /// Generates an entirely new assembly containing a method and all dependencies (variables, methods, classes) needed for it to function in isolation
    /// </summary>
    internal static class ShallowAssemblyBuilder
    {
        public static Assembly GenerateShallowMethodAssembly(MethodInfo methodInfo)
        {
            // Load in all valid IL op codes
            Globals.LoadOpCodes();

            // Track all types discovered that need to be re-generated, and a map of old type name -> re-generated type
            var typeList = new List<Type>();
            var generatedTypes = new Dictionary<string, Type>();

            // Create a Type inspection queue, beginning with the method's parent class
            var queue = new Queue<Type>(new[] { methodInfo.DeclaringType });

            // To start off, queue up all parameter types
            foreach (var paramType in methodInfo.GetParameters())
            {
                queue.Enqueue(paramType.ParameterType);
            }

            // Now collect all of the types that are referenced within local variables
            foreach (var localVar in methodInfo.GetMethodBody().LocalVariables)
            {
                queue.Enqueue(localVar.LocalType);
            }

            // Process all recursive types that are discovered
            while (queue.Count > 0)
            {
                var cursor = queue.Dequeue();

                // Do not attempt to re-generated System types
                if (cursor.Module.ScopeName.Contains("System."))
                    continue;

                // Only add to the queue if it doesn't already exist
                if (!typeList.Contains(cursor))
                {
                    typeList.Add(cursor);

                    // Look at each member type to this type
                    foreach (var member in cursor.GetMembers(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (member.MemberType == MemberTypes.Property)
                        {
                            var property = (PropertyInfo)member;
                            queue.Enqueue(property.PropertyType);
                        }

                        if (member.MemberType == MemberTypes.Field)
                        {
                            var field = (FieldInfo)member;
                            queue.Enqueue(field.FieldType);
                        }
                    }
                }
            }

            // Create an ordered list, class Types should be generated first since they are used within other Types
            var orderedList = typeList.OrderBy(x => x.IsClass);

            // Create a new dynamic assembly
            var assemblyName = $"{methodInfo.DeclaringType.Name}_ShallowProxy";
            var name = new AssemblyName(assemblyName);
            var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule($"{methodInfo.DeclaringType.Name}_ShallowModule");

            var regularAssemblyNamespace = methodInfo.DeclaringType.Namespace;

            // Loop through each discovered Type and generate copies
            foreach (var type in orderedList)
            {
                // Ensure it has not already been re-generated
                if(!generatedTypes.ContainsKey(type.FullName))
                {
                    // Generate the Type copy
                    var typeResult = GenerateType(module, type, generatedTypes);

                    // Check (again) for existence (since GenerateType messes with things, too)
                    if (!generatedTypes.ContainsKey(type.FullName))
                    {
                        // If re-generation succeeded, add it to the map
                        if (typeResult != null)
                        {
                            generatedTypes.Add(type.FullName, typeResult);
                        }
                    }
                }
            }

            // The below code generates a Program class and constructs a Main method that calls the root method that is being exported
            var methodParams = methodInfo.GetParameters();
            var prog = module.DefineType("MethodContainerizer.Program", TypeAttributes.Public | TypeAttributes.Class, null);
            var mainFunc = prog.DefineMethod(
                "Main", 
                MethodAttributes.Public, 
                CallingConventions.Standard, 
                GetOrGenerateType(module, methodInfo.ReturnType, generatedTypes), 
                methodParams.Select(x => 
                    GetOrGenerateType(module, x.ParameterType, generatedTypes)
                ).ToArray()
            );
            
            foreach(var para in methodParams)
            {
                mainFunc.DefineParameter(para.Position, para.Attributes, para.Name);
            }

            var mainIL = mainFunc.GetILGenerator();

            mainIL.Emit(OpCodes.Ldarg_0);

            for(var i = 0; i < methodParams.Length; i++)
            {
                mainIL.Emit(OpCodes.Ldarg, i + 1);
            }

            mainIL.Emit(OpCodes.Callvirt, GetOrGenerateType(module, methodInfo.DeclaringType, generatedTypes).GetMethod(methodInfo.Name));
            mainIL.Emit(OpCodes.Ret);

            // Done!
            prog.CreateType();

            return assembly;
        }

        /// <summary>
        /// Creates a clone of a given Type and adds it to the mapping
        /// </summary>
        private static Type GenerateType(ModuleBuilder module, Type type, Dictionary<string, Type> generatedTypes)
        {
            // Valid types must include at least one method, property, or field that is not a System Type or internal property method setters/getters
            var validMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(t => !t.Module.Assembly.FullName.Contains("System.") && !t.Name.StartsWith("get_") && !t.Name.StartsWith("set_"));

            var validProperties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => !t.Module.Assembly.FullName.Contains("System."));

            var validFields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => !t.Module.Assembly.FullName.Contains("System.") && !t.Name.Contains("BackingField"));

            if (validMethods.Any() || validProperties.Any() || validFields.Any())
            {
                var regularAssemblyName = type.Namespace;
             
                // Recursion safety - if this already exists, return it
                if (module.GetType($"{regularAssemblyName}_Shallow.{type.Name}") != null)
                    return module.GetType($"{regularAssemblyName}_Shallow.{type.Name}");

                // Create a new spot in the isolated assembly for this Type
                var dynamicType = module.DefineType(
                    $"{regularAssemblyName}_Shallow.{type.Name}", 
                    type.Attributes, 
                    GetOrGenerateType(module, type.DeclaringType, generatedTypes) //Attempt to generate the parent as well
                );

                // Loop through all located fields and re-construct
                foreach (var field in validFields)
                {
                    Type fieldType = GetOrGenerateType(module, field.FieldType, generatedTypes);
                    dynamicType.DefineField(field.Name, fieldType, field.Attributes);
                }

                // Loop through all methods and re-compile internal IL instructions
                foreach (var method in validMethods)
                {
                    Type returnType = GetOrGenerateType(module, method.ReturnType, generatedTypes);

                    var methodParameters = method.GetParameters();
                    var methodBodyReader = new MethodBodyReader(method);

                    if (methodBodyReader.instructions != null)
                    {
                        var meth = dynamicType.DefineMethod(
                            method.Name,
                            method.Attributes,
                            method.CallingConvention,
                            returnType,
                            methodParameters.Select(x =>
                                GetOrGenerateType(module, x.ParameterType, generatedTypes)
                            ).ToArray()
                        );

                        foreach(var parameter in methodParameters)
                        {
                            meth.DefineParameter(parameter.Position, parameter.Attributes, parameter.Name);
                        }

                        var il = meth.GetILGenerator();
                        il.UsingNamespace(method.DeclaringType.Namespace);

                        foreach (var instruction in methodBodyReader.instructions)
                        {
                            if (instruction.Operand is string str)
                            {
                                il.Emit(instruction.Code, str);
                            }
                            else if (instruction.Operand is int i)
                            {
                                il.Emit(instruction.Code, i);
                            }
                            else if (instruction.Operand is byte b)
                            {
                                il.Emit(instruction.Code, b);
                            }
                            else if (instruction.Operand is MethodInfo methodInfo1)
                            {
                                var parent = GetOrGenerateType(module, methodInfo1.DeclaringType, generatedTypes);
                                il.Emit(instruction.Code, parent.GetMethod(methodInfo1.Name, methodInfo1.GetParameters().Select(x => GetOrGenerateType(module, x.ParameterType, generatedTypes)).ToArray()));
                            }
                            else if(instruction.Operand is FieldInfo fieldInfo)
                            {
                                try
                                {
                                    var parent = GetOrGenerateType(module, fieldInfo.DeclaringType, generatedTypes);
                                    il.Emit(instruction.Code, parent.GetField(fieldInfo.Name));
                                } catch(Exception)
                                {
                                    // TODO: Anything to do here?
                                }
                            }
                            else
                            {
                                il.Emit(instruction.Code);
                            }
                        }
                    }
                }

                // Loop through each Property and custom-build each getter/setter
                foreach (var property in validProperties)
                {
                    Type propType = GetOrGenerateType(module, property.PropertyType, generatedTypes);

                    var dynamicProperty = dynamicType.DefineProperty(property.Name, property.Attributes, propType, null);
                    var getMethod = property.GetGetMethod();
                    var setMethod = property.GetSetMethod();

                    var fieldBuilder = dynamicType.DefineField(property.Name.ToCamelCase(), propType, FieldAttributes.Private);

                    if (getMethod != null)
                    {
                        var getter = dynamicType.DefineMethod(
                            $"get_{property.Name}",
                            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                            propType,
                            Type.EmptyTypes
                        );

                        var il = getter.GetILGenerator();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, fieldBuilder);
                        il.Emit(OpCodes.Ret);

                        dynamicProperty.SetGetMethod(getter);
                    }

                    if (setMethod != null)
                    {
                        var setter = dynamicType.DefineMethod(
                            $"set_{property.Name}",
                            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                            null,
                            new Type[] { propType }
                        );

                        var il = setter.GetILGenerator();

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Stfld, fieldBuilder);
                        il.Emit(OpCodes.Ret);

                        dynamicProperty.SetSetMethod(setter);
                    }
                }

                try
                {
                    return dynamicType.CreateType();
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursive. Checks to see if a Type has been generated - return if so, otherwise, generate it. This is ran a lot.
        /// </summary>
        private static Type GetOrGenerateType(ModuleBuilder module, Type type, Dictionary<string, Type> generatedTypes)
        {
            if (type == null)
                return null;

            if (generatedTypes.ContainsKey(type.FullName))
            {
                return generatedTypes[type.FullName];
            }
            else
            {
                if (type.Module.Assembly.FullName.Contains("System."))
                {
                    return type;
                }
                else
                {
                    var typeResult = GenerateType(module, type, generatedTypes);
                    if(typeResult != null)
                    {
                        generatedTypes.Add(type.FullName, typeResult);
                        return typeResult;
                    }

                    return type;
                }
            }
        }
    }
}