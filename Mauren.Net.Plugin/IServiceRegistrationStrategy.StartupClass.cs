using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

namespace Mauren.Net.Plugin
{
    public class StartupClassRegistrationStrategy(IConfiguration configuration) : IServiceRegistrationStrategy
    {
        public Predicate<Type> ProvidesServices => (Type type) =>
        {
            // If the type's name does not end in 'Startup'
            if (type.Name.EndsWith("Startup", StringComparison.OrdinalIgnoreCase) is false)
                return false;

            ConstructorInfo[] cs = type.GetConstructors();

            BindingFlags constructorBindingFlags = BindingFlags.Instance | BindingFlags.Public;
            Binder? constructorBinder = null;
            CallingConventions constructorCallingConvention = CallingConventions.Any;
            Type[] constructorTypes = [typeof(IConfiguration)];
            ParameterModifier[]? constructorModifiers = null;
            ConstructorInfo? constructorInfo = type.GetConstructor(constructorBindingFlags, constructorBinder, constructorCallingConvention, constructorTypes, constructorModifiers);
            var idiomatic = type.GetConstructor(constructorTypes);
            if (constructorInfo is null)
                return false;

            String name = "ConfigureServices";
            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            Binder? binder = null;
            Type[] types = [typeof(IServiceCollection)];
            ParameterModifier[]? modifiers = null;
            // If the type does not have a 'void ConfigureServices(IServiceCollection _)' method
            if (type.GetMethod(name, bindingFlags, binder, types, modifiers) is not MethodInfo methodInfo || methodInfo.ReturnType != typeof(void))
                return false;

            // If the type is not a class or is abstract
            if (!type.IsClass || type.IsAbstract)
                return false;

            return true;
        };

        public void Apply(Type type, IServiceCollection services)
        {
            // Create an instance of the manifest type
            Object? instance = Activator.CreateInstance(type, configuration);
            // If the instance is not a manifest, skip
            if (instance is not Object startup)
                return;

            String methodName = "ConfigureServices";
            BindingFlags methodBindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            Binder? methodBinder = null;
            Type[] methodTypes = [typeof(IServiceCollection)];
            ParameterModifier[]? methodModifiers = null;
            // If the instance does not have a ConfigureServices method
            if (type.GetMethod(methodName, methodBindingFlags, methodBinder, methodTypes, methodModifiers) is not MethodInfo methodInfo)
                return;
            // Invoke the ConfigureServices method
            Object? _ = methodInfo.Invoke(instance, [services]);
        }
    }
}
