using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Babel;
using Babel.Xml;

namespace IncrementalRenaming
{
    class IncrementalRenamingListner : IBabelRenamingListener
    {
        private List<ISymbolDef> _notFound;

        public XmlMapElement AssemblyElement { get; set; }

        public IList<ISymbolDef> SymbolsNotFound
        {
            get
            {
                return _notFound;
            }
        }

        public IncrementalRenamingListner()
        {
            _notFound = new List<ISymbolDef>();
        }

        public void OnSymbolRenaming(ISymbolDef symbol, RenamingArguments info)
        {
            var element = FindSymbolElement(symbol);
            if (element == null)
            {
                if (symbol.IsBabelGenerated)
                    return;

                _notFound.Add(symbol);
                return;
            }

            string newName = element.NewName;

            info.NewName = newName;
            info.Cancel = string.IsNullOrEmpty(newName);
        }

        public void OnSymbolRenamed(ISymbolDef symbol)
        {
            
        }

        private XmlMapElement FindSymbolElement(ISymbolDef symbol)
        {
            if (symbol.IsTypeDef)
                return FindTypeElement((TypeDef)symbol);
            else if (symbol.IsMethodDef)
                return FindMethodElement((MethodDef)symbol);
            if (symbol.IsParameterDef)
                return FindParameterElement((ParameterDef)symbol);
            else if (symbol.IsFieldDef)
                return FindFieldElement((FieldDef)symbol);
            else if (symbol.IsPropertyDef)
                return FindPropertyElement((PropertyDef)symbol);
            else if (symbol.IsEventDef)
                return FindEventElement((EventDef)symbol);
            else if (symbol.IsGenericParam)
                return FindGenericParamElement((GenericParam)symbol);

            return null;
        }

        private XmlMapElement FindTypeElement(TypeDef type)
        {
            var nestedStack = new Stack<TypeDef>();
            nestedStack.Push(type);

            while (type.IsNested)
            {
                type = type.DeclaringType;
                nestedStack.Push(type);
            }

            type = nestedStack.Peek();
            var current = FindElementByName(AssemblyElement, type.OriginalNamespace);

            if (current != null)
            {
                do
                {
                    type = nestedStack.Pop();
                    current = FindElementByName(current, type.OriginalName);

                    if (current == null)
                        break;

                } while (nestedStack.Count > 0);
            }

            return current;
        }

        private XmlMapElement FindMethodElement(MethodDef method)
        {
            XmlMapElement elType = FindTypeElement(method.DeclaringType);
            if (elType != null)
            {
                // Get all methods 
                var elMethods = elType.Descendants().Where(item => 
                                    item.Name == method.OriginalName && 
                                    item.IsAnyMethod && 
                                    item.IsVirtual == method.IsVirtual && 
                                    item.IsStatic == method.IsStatic);

                var parameters = method.Parameters.ToArray();

                foreach (var elMethod in elMethods)
                {
                    var elParameters = GetParameters(elMethod);
                    if (AreParametersEquals(elParameters, parameters))
                    {
                        var elReturnType = GetElementReturnType(elMethod);
                        if (AreTypeEquals(elReturnType, method.ReturnType))
                            return elMethod;
                    }
                }
            }
            return null;
        }

        private XmlMapElement FindPropertyElement(PropertyDef property)
        {
            XmlMapElement elType = FindTypeElement(property.DeclaringType);
            if (elType != null)
            {
                // Get all methods 
                var elProperties = elType.Elements().Where(item => item.IsProperty &&
                                                           item.Name == property.OriginalName);                
                
                foreach (var elProperty in elProperties)
                {
                    if (AreTypeEquals(elProperty, property.PropertyType))
                    {
                        if (property.HasGet)
                        {
                            var elGet = elProperty.Elements().FirstOrDefault(el => el.IsGet);
                            if (elGet == null)
                                continue;

                            if (!AreMethodEquals(elGet, property.Get))
                                continue;
                        }

                        if (property.HasSet)
                        {
                            var elSet = elProperty.Elements().FirstOrDefault(el => el.IsSet);
                            if (elSet == null)
                                continue;

                            if (!AreMethodEquals(elSet, property.Set))
                                continue;
                        }

                        return elProperty;
                    }
                }
            }

            return null;
        }

        private XmlMapElement FindEventElement(EventDef evt)
        {
            XmlMapElement elType = FindTypeElement(evt.DeclaringType);
            if (elType != null)
            {
                // Get all methods 
                var elEvents = elType.Elements().Where(item => item.IsEvent &&
                                                           item.Name == evt.Name);

                foreach (var elEvent in elEvents)
                {
                    if (AreTypeEquals(elEvent, evt.EventType))
                        return elEvent;
                }
            }

            return null;
        }

        private XmlMapElement FindFieldElement(FieldDef field)
        {
            XmlMapElement elType = FindTypeElement(field.DeclaringType);
            if (elType != null)
            {
                // Get all methods 
                var elFields = elType.Elements().Where(item => item.IsField &&
                                                           item.Name == field.OriginalName &&
                                                           item.IsStatic == field.IsStatic);

                foreach (var elField in elFields)
                {
                    if (AreTypeEquals(elField, field.FieldType))
                        return elField;
                }
            }
            return null;
        }

        private XmlMapElement FindGenericParamElement(GenericParam symbol)
        {
            XmlMapElement[] elGenericParameters = new XmlMapElement[0];
            var owner = symbol.Owner;
            if (owner is TypeDef)
            {
                TypeDef type = (TypeDef)owner;
                XmlMapElement elType = FindTypeElement(type);
                if (elType != null)
                {
                    elGenericParameters = GetGenericParameters(elType);
                }
            }
            else if (owner is MethodDef)
            {
                MethodDef method = (MethodDef)owner;
                XmlMapElement elMethod = FindMethodElement(method);
                if (elMethod != null)
                {
                    elGenericParameters = GetGenericParameters(elMethod);
                }
            }

            if (symbol.Position >= elGenericParameters.Length)
                return null;

            return elGenericParameters[symbol.Position];
        }

        private XmlMapElement FindParameterElement(ParameterDef symbol)
        {
            var method = symbol.Method;
            if (method.IsDefinition)
            {
                XmlMapElement elMethod = FindMethodElement((MethodDef)method);
                if (elMethod != null)
                {
                    var elParameters = GetParameters(elMethod);
                    if (symbol.Index < elParameters.Length)
                        return elParameters[symbol.Index];
                }
            }
            return null;
        }

        private static XmlMapElement FindElementByName(XmlMapElement parent, string name)
        {
            return parent.Elements().Where(item => item.Name == name).FirstOrDefault();
        }

        private static XmlMapElement[] GetParameters(XmlMapElement parent)
        {
            var parameters = parent.Elements().FirstOrDefault(item => item.IsParameters);
            if (parameters == null)
                return new XmlMapElement[0];

            return parameters.Elements().Where(item => item.IsParameter).ToArray();
        }

        private static XmlMapElement[] GetGenericParameters(XmlMapElement parent)
        {
            var parameters = parent.Elements().FirstOrDefault(item => item.IsGenericParameters);
            if (parameters == null)
                return new XmlMapElement[0];

            return parameters.Elements().Where(item => item.IsGenericParameter).ToArray();
        }

        private static XmlMapElement GetElementReturnType(XmlMapElement parent)
        {
            return parent.Elements().Where(item => item.IsReturnType).FirstOrDefault();
        }

        private static bool AreParametersEquals(XmlMapElement[] elParameters, ParameterDef[] parameters)
        {
            if (elParameters.Length != parameters.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (!AreTypeEquals(elParameters[i], parameterType))
                    return false;
            }
            return true;
        }

        private static bool AreTypeEquals(XmlMapElement par, IType type)
        {
            string fullName = par.Type;
            if (fullName == null)
                return false;

            return type.OriginalFullName == fullName;
        }

        private static bool AreMethodEquals(XmlMapElement elMethod, MethodDef method)
        {
            if (elMethod.Name != method.OriginalName)
                return false;

            if (elMethod.IsVirtual != method.IsVirtual)
                return false;

            if (elMethod.IsStatic != method.IsStatic)
                return false;

            if (elMethod.IsGeneric != method.IsGeneric)
                return false;

            var elReturnType = GetElementReturnType(elMethod);
            if (!AreTypeEquals(elReturnType, method.ReturnType))
                return false;

            var parameters = method.Parameters.ToArray();
            var elParameters = GetParameters(elMethod);

            if (!AreParametersEquals(elParameters, parameters))
                return false;
            
            return true;
        }
    }
}
