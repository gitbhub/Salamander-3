﻿using Nucleus.Actions;
using Nucleus.Base;
using Nucleus.Extensions;
using Nucleus.Geometry;
using Nucleus.Model;
using Nucleus.Rhino;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Salamander.Actions;
using Salamander.Display;
using Salamander.Rhino;
using Salamander.RhinoCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using RC = Rhino.Geometry;
using System.Windows.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Display;
using Salamander.Resources;

namespace Salamander.Grasshopper
{
    /// <summary>
    /// Base class for Salamander 3 components
    /// </summary>
    public abstract class SalamanderBaseComponent : GH_Component
    {
        #region 

        /// <summary>
        /// The name of the tab on which components are, by default, to be placed
        /// </summary>
        public const string CategoryName = "Salamander 3";

        #endregion

        #region Properties

        /// <summary>
        /// The name of the command executed by this component
        /// </summary>
        public string CommandName { get; private set; }

        /// <summary>
        /// The type of action wrapped by this component
        /// </summary>
        public Type ActionType { get; private set; }

        /// <summary>
        /// The last action successfully executed by this component
        /// </summary>
        private IAction LastExecuted { get; set; }

        /// <summary>
        /// The execution information for the last execution
        /// </summary>
        private ExecutionInfo LastExecutionInfo { get; set; }

        /// <summary>
        /// Override this to return true if the outputs of this component should be cached
        /// in the collection returned by the OutputCache property, which should also be overridden
        /// </summary>
        protected virtual bool CacheOutputs { get { return false; } }

        /// <summary>
        /// Override this to return a collection to use to cache the outputs of the component
        /// </summary>
        protected virtual IList OutputCache
        {
            get { return null; }
        }

        private GH_Exposure _Exposure = GH_Exposure.primary;

        /// <summary>
        /// The exposure of the component
        /// </summary>
        public override GH_Exposure Exposure
        {
            get
            {
                return _Exposure;
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Internal_Icon_24x24
        {
            get
            {
                var actionAtt = ActionAttribute.ExtractFrom(ActionType);
                if (!string.IsNullOrWhiteSpace(actionAtt.IconForeground))
                {
                    if (!string.IsNullOrWhiteSpace(actionAtt.IconBackground))
                    {
                        return IconResourceHelper.CombinedBitmapFromURIs(actionAtt.IconBackground, actionAtt.IconForeground);
                    }
                    else return IconResourceHelper.BitmapFromURI(actionAtt.IconForeground);
                }
                else if (!string.IsNullOrWhiteSpace(actionAtt.IconBackground)) return IconResourceHelper.BitmapFromURI(actionAtt.IconBackground);
                return base.Internal_Icon_24x24;
            }
        }

        /// <summary>
        /// The display layer used to draw the preview for this component
        /// </summary>
        protected DisplayLayer PreviewLayer { get; set; } = null;


        #endregion

        #region Constructors

        ///// <summary>
        ///// Constructor.  Call this from the default constructor of all subclasses, passing in the required information.
        ///// </summary>
        ///// <param name="commandName">The command name of the action that this component invokes</param>
        ///// <param name="name">The name of this component.  Keep it simple, single words are best.</param>
        ///// <param name="nickname">The abbreviation of this component.  Keep it short, 1-5 characters are best</param>
        ///// <param name="description">The description of this component.  Be succinct but clear.  You can supply whole sentances.</param>
        ///// <param name="category">The category of this component.  Controls which tab components will end up in.</param>
        ///// <param name="subCategory">The subcategory for this component.  Controls which group the component will be in.</param>
        //protected NewtBaseComponent(string commandName, string name, string nickname, string description,
        //    string subCategory, string category = CategoryName)
        //  : base()
        //{
        //    CommandName = commandName;
        //    Host.EnsureInitialisation();
        //    ActionType = Core.Instance.Actions.GetActionDefinition(CommandName);
        //    Name = name;
        //    NickName = nickname;
        //    Description = description;
        //    Category = category;
        //    SubCategory = subCategory;
        //    PostConstructor();
        //}

        /// <summary>
        /// Constructor.  Call this from the default constructor of all subclasses, passing in the required information.
        /// </summary>
        /// <param name="commandName">The command name of the action that this component invokes</param>
        /// <param name="name">The name of this component.  Keep it simple, single words are best.</param>
        /// <param name="nickname">The abbreviation of this component.  Keep it short, 1-5 characters are best</param>
        /// <param name="category">The category of this component.  Controls which tab components will end up in.</param>
        /// <param name="subCategory">The subcategory for this component.  Controls which group the component will be in.</param>
        protected SalamanderBaseComponent(string commandName, string name, string nickname,
            string subCategory, GH_Exposure exposure = GH_Exposure.primary, string category = CategoryName)
          : base()
        {
            CommandName = commandName;
            Host.EnsureInitialisation(true);
            ActionType = Core.Instance.Actions.GetActionDefinition(CommandName);
            if (ActionType == null) throw new Exception("Command '" + CommandName + "' has not been found.  The plugin that contains it may not have been successfully loaded.");
            var attributes = ActionAttribute.ExtractFrom(ActionType);
            Name = name;
            NickName = nickname;
            Description = attributes.Description.CapitaliseFirst();
            Category = category;
            SubCategory = subCategory;
            _Exposure = exposure;
            if (attributes.PreviewLayerType != null)
                PreviewLayer = Activator.CreateInstance(attributes.PreviewLayerType) as DisplayLayer;
            PostConstructor();
        }

        #endregion


        #region Methods

        /// <summary>
        /// Get the appropriate GH_ParamAccess value for a collection type with the given ActionInput attribute
        /// </summary>
        /// <param name="inputAtt"></param>
        /// <returns></returns>
        private GH_ParamAccess ParamAccess(ActionInputAttribute inputAtt)
        {
            if (inputAtt.OneByOne) return GH_ParamAccess.item;
            else return GH_ParamAccess.list;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            IAction action = (IAction)Activator.CreateInstance(ActionType, true);
            NicknameConverter nC = new NicknameConverter();
            IList<PropertyInfo> inputs = ActionBase.ExtractInputParameters(ActionType);
            foreach (PropertyInfo pInfo in inputs)
            {
                Type pType = pInfo.PropertyType;
                ActionInputAttribute inputAtt = ActionInputAttribute.ExtractFrom(pInfo);
                if (inputAtt != null && inputAtt.Parametric)
                {
                    string name = pInfo.Name;
                    string nickname = string.IsNullOrEmpty(inputAtt.ShortName) ? nC.Convert(pInfo.Name) : inputAtt.ShortName;
                    string description = inputAtt.CapitalisedDescription;
                    if (pType == typeof(double))
                    {
                        pManager.AddNumberParameter(name, nickname, description, GH_ParamAccess.item, (double)pInfo.GetValue(action, null));
                    }
                    else if (pType == typeof(int))
                    {
                        pManager.AddIntegerParameter(name, nickname, description, GH_ParamAccess.item, (int)pInfo.GetValue(action, null));
                    } 
                    else if (pType == typeof(string))
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item, (string)pInfo.GetValue(action, null));
                    }
                    else if (pType == typeof(bool))
                    {
                        //Special case when the input is a 'Write' toggle - default off!
                        pManager.AddBooleanParameter(name, nickname, description, GH_ParamAccess.item, name == "Write" ? false : (bool)pInfo.GetValue(action, null));
                    }
                    else if (pType == typeof(Vector))
                    {
                        pManager.AddPointParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(IList<Vector>).IsAssignableFrom(pType)) //Vector list
                    {
                        pManager.AddPointParameter(name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (pType.IsAssignableFrom(typeof(Plane)))
                    {
                        pManager.AddPlaneParameter(name, nickname, description, GH_ParamAccess.item, ToRC.Convert((Plane)pInfo.GetValue(action, null)));
                    }
                    else if (pType == typeof(Line))
                    {
                        pManager.AddLineParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(Angle))
                    {
                        pManager.AddAngleParameter(name, nickname, description, GH_ParamAccess.item, (Angle)pInfo.GetValue(action, null));
                    }
                    else if (typeof(Curve).IsAssignableFrom(pType))
                    {
                        pManager.AddCurveParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(CurveCollection).IsAssignableFrom(pType))
                    {
                        pManager.AddCurveParameter(name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (typeof(LinearElement).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new LinearElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(PanelElement).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new PanelElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(Element).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new ElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(LinearElementCollection).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new LinearElementParam();
                        pManager.AddParameter(param, name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (typeof(PanelElementCollection).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new PanelElementParam();
                        pManager.AddParameter(param, name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (typeof(ElementCollection).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new ElementParam();
                        pManager.AddParameter(param, name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (pType == typeof(Node))
                    {
                        IGH_Param param = new NodeParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(NodeCollection))
                    {
                        IGH_Param param = new NodeParam();
                        pManager.AddParameter(param, name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (typeof(Nucleus.Model.Material).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new MaterialParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(MaterialCollection).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new MaterialParam();
                        pManager.AddParameter(param, name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (typeof(SectionFamily).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new SectionFamilyParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(Bool6D))
                    {
                        IGH_Param param = new Bool6DParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(FilePath))
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(VertexGeometry))
                    {
                        pManager.AddGeometryParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(VertexGeometryCollection))
                    {
                        pManager.AddGeometryParameter(name, nickname, description, ParamAccess(inputAtt));
                    }
                    else if (pType == typeof(ActionTriggerInput))
                    {
                        pManager.AddGenericParameter(name, nickname, description, GH_ParamAccess.tree);
                    }
                    else if (pType == typeof(Direction))
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item, pInfo.GetValue(action, null)?.ToString());
                    }
                    else if (pType == typeof(CoordinateSystemReference))
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item, pInfo.GetValue(action, null)?.ToString());
                    }
                    else if (pType.IsEnum)
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item, pInfo.GetValue(action, null)?.ToString());
                    }
                    else
                    {
                        pManager.AddGenericParameter(pInfo.Name, nickname, description, GH_ParamAccess.item);
                    }

                    if (inputAtt.Required == false)
                        pManager[pManager.ParamCount - 1].Optional = true;

                    //TODO
                }
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            NicknameConverter nC = new NicknameConverter();
            IList<PropertyInfo> outputs = ActionBase.ExtractOutputParameters(ActionType);
            foreach (PropertyInfo pInfo in outputs)
            {
                Type pType = pInfo.PropertyType;
                ActionOutputAttribute outputAtt = ActionOutputAttribute.ExtractFrom(pInfo);
                if (outputAtt != null)
                {
                    string name = pInfo.Name;
                    string nickname = string.IsNullOrEmpty(outputAtt.ShortName) ? nC.Convert(pInfo.Name) : outputAtt.ShortName;
                    string description = outputAtt.CapitalisedDescription;
                    if (pType == typeof(double) || pType == typeof(Angle))
                    {
                        pManager.AddNumberParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(int))
                    {
                        pManager.AddIntegerParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(string))
                    {
                        pManager.AddTextParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(bool))
                    {
                        pManager.AddBooleanParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(Vector))
                    {
                        pManager.AddPointParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType.IsAssignableFrom(typeof(Plane)))
                    {
                        pManager.AddPlaneParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(Curve).IsAssignableFrom(pType))
                    {
                        pManager.AddCurveParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(CurveCollection).IsAssignableFrom(pType))
                    {
                        pManager.AddCurveParameter(name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (pType == typeof(LinearElement))
                    {
                        IGH_Param param = new LinearElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(LinearElementCollection))
                    {
                        IGH_Param param = new LinearElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (pType == typeof(Element))
                    {
                        IGH_Param param = new ElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(ElementCollection))
                    {
                        IGH_Param param = new ElementParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (pType == typeof(Node))
                    {
                        IGH_Param param = new NodeParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(NodeCollection))
                    {
                        IGH_Param param = new NodeParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (typeof(Nucleus.Model.Material).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new MaterialParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (typeof(MaterialCollection).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new MaterialParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.list);
                    }
                    else if (typeof(SectionFamily).IsAssignableFrom(pType))
                    {
                        IGH_Param param = new SectionFamilyParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(Bool6D))
                    {
                        IGH_Param param = new Bool6DParam();
                        pManager.AddParameter(param, name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(VertexGeometry))
                    {
                        pManager.AddGeometryParameter(name, nickname, description, GH_ParamAccess.item);
                    }
                    else if (pType == typeof(VertexGeometryCollection))
                    {
                        pManager.AddGeometryParameter(name, nickname, description, GH_ParamAccess.list);
                    }
                    else
                    {
                        pManager.AddGenericParameter(pInfo.Name, nC.Convert(pInfo.Name), outputAtt.CapitalisedDescription, GH_ParamAccess.item);
                    }
                    //TODO
                }
            }
        }

        protected override void BeforeSolveInstance()
        {
            if (PreviewLayer != null) PreviewLayer.Clear();
            base.BeforeSolveInstance();
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Host.EnsureInitialisation();
            LastExecuted = null;
            if (ActionType != null)
            {
                IAction action = (IAction)Activator.CreateInstance(ActionType, true);
                if (action != null)
                {
                    if (action is IModelDocumentAction && !GrasshopperManager.Instance.AutoBake)
                    {
                        IModelDocumentAction mDAction = (IModelDocumentAction)action;
                        mDAction.Document = GrasshopperManager.Instance.BackgroundDocument(OnPingDocument());
                    }

                    if (ExtractInputs(action, DA))
                    {
                        ExecutionInfo exInfo = new ExecutionInfo(InstanceGuid.ToString(), DA.Iteration);
                        if (action.PreExecutionOperations(exInfo))
                            if (action.Execute(exInfo))
                                if (action.PostExecutionOperations(exInfo))
                                {
                                    ExtractOutputs(action, DA, exInfo);
                                    LastExecuted = action;
                                    LastExecutionInfo = exInfo;
                                }
                    }
                }
            }
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            //Delete unupdated objects, etc:
            if (LastExecuted != null)
            {
                LastExecuted.FinalOperations(LastExecutionInfo);
            }
        }

        /// <summary>
        /// Automatically populate action inputs using data extracted from the Grasshopper Data Access object.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="DA"></param>
        protected bool ExtractInputs(IAction action, IGH_DataAccess DA)
        {
            IList<PropertyInfo> inputs = ActionBase.ExtractInputParameters(ActionType);
            foreach (PropertyInfo pInfo in inputs)
            {
                ActionInputAttribute inputAtt = ActionInputAttribute.ExtractFrom(pInfo);

                if (inputAtt.Parametric) //Ignore non-parametric inputs
                {
                    Type inputType = pInfo.PropertyType;
                    object inputData = GetInputData(pInfo.Name, inputType, DA, inputAtt);
                    if (!inputAtt.Required || inputData != null)
                        pInfo.SetValue(action, inputData, null);
                    else return false;
                }
            }
            return true;
        }

        protected bool ExtractOutputs(IAction action, IGH_DataAccess DA, ExecutionInfo exInfo)
        {
            IList<PropertyInfo> outputs = ActionBase.ExtractOutputParameters(ActionType);
            foreach (PropertyInfo pInfo in outputs)
            {
                object outputData = pInfo.GetValue(action, null);
                if (PreviewLayer != null)
                {
                    if (outputData is ICollection) //TODO: This would currently catch mesh faces and other similar objects...
                    {
                        PreviewLayer.TryRegisterAll((ICollection)outputData);
                    }
                    else
                    { 
                        PreviewLayer.TryRegister(outputData);
                    }
                }
                outputData = FormatForOutput(outputData, exInfo);
                if (outputData is IList)
                {
                    DA.SetDataList(pInfo.Name, (IEnumerable)outputData);
                }
                else DA.SetData(pInfo.Name, outputData);

            }
            return true;
        }

        /// <summary>
        /// Extract data from the Grasshopper Data Access object, converting it into the specified type where necessary
        /// </summary>
        /// <param name="index"></param>
        /// <param name="type"></param>
        /// <param name="DA"></param>
        /// <returns></returns>
        protected object GetInputData(string name, Type type, IGH_DataAccess DA, ActionInputAttribute inputAtt)
        {
            object result = null;
            if (type != typeof(ActionTriggerInput))
            {
                try
                {
                    // TODO: equivalent for GetDataList
                    Type equivalentType = GetEquivalentType(type);
                    MemberInfo[] mInfos;
                    var pAccess = ParamAccess(inputAtt);
                    if (pAccess == GH_ParamAccess.item)
                        mInfos = typeof(IGH_DataAccess).GetMember("GetData");
                    else
                    {
                        var listType = typeof(List<>);
                        var constructedListType = listType.MakeGenericType(equivalentType);
                        result = Activator.CreateInstance(constructedListType);
                        mInfos = typeof(IGH_DataAccess).GetMember("GetDataList");
                    }
                    //TODO: Trees?
                    MethodInfo getDataMethod = null;
                    foreach (MethodInfo mInfo in mInfos)
                    {
                        Type firstPT = mInfo.GetParameters()[0].ParameterType;
                        if (firstPT == typeof(string)) getDataMethod = mInfo;
                    }
                    if (getDataMethod != null)
                    {
                        object[] args = new object[] { name, result };
                        MethodInfo getDataGeneric = getDataMethod.MakeGenericMethod(new Type[] { equivalentType });
                        bool success = (bool)getDataGeneric.Invoke(DA, args);
                        if (success)
                        {
                            result = args[1];
                            if (result.GetType() != type)
                            {
                                result = Convert(result, type);
                            }
                        }
                        else result = null;
                    }
                }
                catch (Exception e)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                }
            }
            return result;
        }

        /// <summary>
        /// Wrap an object in goo, if possible
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        protected object FormatForOutput(object obj, ExecutionInfo exInfo)
        {
            if (obj is Vector) return ToRC.Convert((Vector)obj);
            else if (obj is Curve) return ToRC.Convert((Curve)obj);
            else if (obj is CurveCollection) return ToRC.Convert((CurveCollection)obj);
            else if (obj is VertexGeometry) return ToRC.Convert((VertexGeometry)obj);
            else if (obj is VertexGeometryCollection) return ToRC.Convert((VertexGeometryCollection)obj);
            else if (obj is LinearElement) return new LinearElementGoo((LinearElement)obj, exInfo);
            else if (obj is LinearElementCollection) return LinearElementGoo.Convert((LinearElementCollection)obj);
            else if (obj is PanelElement) return new PanelElementGoo((PanelElement)obj);
            else if (obj is PanelElementCollection) return PanelElementGoo.Convert((PanelElementCollection)obj);
            else if (obj is Element) return new ElementGoo((Element)obj);
            else if (obj is ElementCollection) return ElementGoo.Convert((ElementCollection)obj);
            else if (obj is SectionFamily) return new SectionFamilyGoo((SectionFamily)obj);
            else if (obj is SectionFamilyCollection) return SectionFamilyGoo.Convert((SectionFamilyCollection)obj);
            else if (obj is BuildUpFamily) return new BuildUpFamilyGoo((BuildUpFamily)obj);
            else if (obj is BuildUpFamilyCollection) return BuildUpFamilyGoo.Convert((BuildUpFamilyCollection)obj);
            else if (obj is Node) return new NodeGoo((Node)obj, exInfo);
            else if (obj is NodeCollection) return NodeGoo.Convert((NodeCollection)obj, exInfo);
            else if (obj is Nucleus.Model.Material) return new MaterialGoo((Nucleus.Model.Material)obj);
            else if (obj is MaterialCollection) return MaterialGoo.Convert((MaterialCollection)obj);
            else if (obj is Bool6D) return new Bool6DGoo((Bool6D)obj);
            else if (obj is FilePath) return obj.ToString();
            //Add more types here
            return obj;
        }

        /// <summary>
        /// Convert the specified object to the specified type.
        /// Override this function to convert to data types not natively supported
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="toType"></param>
        /// <returns></returns>
        /// <remarks>Currently supported:
        ///  DesignLink.Types.Line</remarks>
        protected virtual object Convert(object obj, Type toType)
        {
            //if (obj is SectionFamilyGoo && typeof(SectionFamily).IsAssignableFrom(toType)) return ((SectionFamilyGoo)obj).Value;
            //if (toType == typeof(LinearElementGoo)) return new LinearElementGoo(obj as LinearElement);
            //else if (toType == typeof(SectionFamily)) return new SectionFamilyGoo(obj as SectionFamily);

            if (obj is ISalamander_Goo) return ((ISalamander_Goo)obj).GetValue(toType); //Single items
            else if (obj is IList && typeof(IList).IsAssignableFrom(toType)
                && typeof(ISalamander_Goo).IsAssignableFrom(obj.GetType().GetItemType()))
            {
                // Collections:
                IList list = (IList)obj;
                IList result = Activator.CreateInstance(toType) as IList;
                Type itemType = obj.GetType().GetItemType();
                foreach (ISalamander_Goo item in list)
                {
                    result.Add(item.GetValue(itemType));
                }
                return result;
            }
            else if (toType == typeof(Angle)) return new Angle((double)obj);
            else if (toType == typeof(FilePath)) return new FilePath(obj.ToString());
            else if (toType == typeof(ActionTriggerInput)) return new ActionTriggerInput();
            else if (toType == typeof(Direction)) return Enum.Parse(typeof(Direction), obj?.ToString());
            else if (toType == typeof(CoordinateSystemReference) && !(obj is CoordinateSystemReference))
            {
                GH_Document doc = OnPingDocument();
                if (doc != null)
                {
                    ModelDocument modelDoc = GrasshopperManager.Instance.BackgroundDocument(doc);
                    return modelDoc?.Model?.CoordinateSystems.GetByKeyword(obj.ToString());
                }
            }
            else if (toType.IsEnum) return Enum.Parse(toType, obj?.ToString());

            return Conversion.Instance.Convert(obj, toType);
            /*
            //From RhinoCommon:
            if (toType.IsAssignableFrom(typeof(Curve)))
            {
                if (obj is RC.Curve) return RCtoFB.Convert((RC.Curve)obj);
            }
            //To RhinoCommon:
            if (toType.IsAssignableFrom(typeof(RC.Point3d)))
            {
                if (obj is Vector) return FBtoRC.Convert((Vector)obj);
            }
            return obj;
            */
        }

        protected virtual Type GetEquivalentType(Type type)
        {
            if (typeof(Line).IsAssignableFrom(type)) return typeof(RC.Line);
            else if (type == typeof(Vector)) return typeof(RC.Point3d);
            else if (typeof(IList<Vector>).IsAssignableFrom(type)) return typeof(RC.Point3d);
            else if (type == typeof(Angle)) return typeof(double);
            else if (typeof(Curve).IsAssignableFrom(type)) return typeof(RC.Curve);
            else if (typeof(CurveCollection).IsAssignableFrom(type)) return typeof(RC.Curve);
            else if (typeof(VertexGeometry).IsAssignableFrom(type)) return typeof(RC.GeometryBase);
            else if (typeof(VertexGeometryCollection).IsAssignableFrom(type)) return typeof(RC.GeometryBase);
            else if (typeof(LinearElement).IsAssignableFrom(type)) return typeof(LinearElementGoo);
            else if (typeof(PanelElement).IsAssignableFrom(type)) return typeof(PanelElementGoo);
            else if (typeof(Element).IsAssignableFrom(type)) return typeof(ElementGoo);
            else if (typeof(LinearElementCollection).IsAssignableFrom(type)) return typeof(LinearElementGoo);
            else if (typeof(PanelElementCollection).IsAssignableFrom(type)) return typeof(PanelElementGoo);
            else if (typeof(ElementCollection).IsAssignableFrom(type)) return typeof(ElementGoo);
            else if (typeof(SectionFamily).IsAssignableFrom(type)) return typeof(SectionFamilyGoo);
            else if (typeof(BuildUpFamily).IsAssignableFrom(type)) return typeof(BuildUpFamilyGoo);
            else if (typeof(Node).IsAssignableFrom(type)) return typeof(NodeGoo);
            else if (typeof(NodeCollection).IsAssignableFrom(type)) return typeof(NodeGoo);
            else if (typeof(Nucleus.Model.Material).IsAssignableFrom(type)) return typeof(MaterialGoo);
            else if (typeof(MaterialCollection).IsAssignableFrom(type)) return typeof(MaterialGoo);
            else if (typeof(Bool6D).IsAssignableFrom(type)) return typeof(Bool6DGoo);
            else if (typeof(Direction).IsAssignableFrom(type)) return typeof(string);
            else if (typeof(FilePath) == type) return typeof(string);
            else if (typeof(ActionTriggerInput) == type) return typeof(object);
            else if (typeof(CartesianCoordinateSystem).IsAssignableFrom(type)) return typeof(RC.Plane);
            else if (typeof(CoordinateSystemReference).IsAssignableFrom(type)) return typeof(string);
            else if (type.IsEnum) return typeof(string);
            return type;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            try
            {
                // Clean up generated objects in both the active and background models:
                Core.Instance?.ActiveDocument?.Model?.History?.DeleteAllFromSource(InstanceGuid.ToString());

                GH_Document doc = OnPingDocument();
                if (doc != null)
                {
                    ModelDocument modelDoc = GrasshopperManager.Instance.BackgroundDocument(doc);
                    modelDoc?.Model?.History?.DeleteAllFromSource(InstanceGuid.ToString());
                }
                else
                {
                    //If we can't lookup the document, we'll have to clear it from all of them!
                    foreach (var kvp in GrasshopperManager.Instance.BackgroundDocuments)
                    {
                        kvp.Value?.Model?.History?.DeleteAllFromSource(InstanceGuid.ToString());
                    }
                }
            }
            catch
            { }

            base.RemovedFromDocument(document);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            ToolStripMenuItem mItem = GH_DocumentObject.Menu_AppendItem(menu, "Modify Main Model", new EventHandler(this.Menu_MainModelClicked), 
                true, GrasshopperManager.Instance.AutoBake);
            mItem.ToolTipText = 
                "If checked, Salamander Grasshopper components will automatically bake to and update the primary Salamander model.  " + Environment.NewLine +
                "Otherwise, changes will be written only to a temporary background model and will not have an effect on the main " + Environment.NewLine
                + "model.  This is a global setting which applies to *all* Salamander components in the document.";
        }

        public void Menu_MainModelClicked(object sender, System.EventArgs e)
        {
            GrasshopperManager.Instance.AutoBake = !GrasshopperManager.Instance.AutoBake;
            GrasshopperManager.Instance.InvalidateAllSalamanderComponents(this.OnPingDocument());
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (PreviewLayer != null)
            {
                DisplayMaterial material;
                if (this.Attributes.GetTopLevel.Selected)
                {
                    material = args.ShadeMaterial_Selected;
                }
                else material = args.ShadeMaterial;
                args.Display.DisplayPipelineAttributes.ShadingEnabled = false;
                PreviewLayer.Draw(new RhinoRenderingParameters(args.Display, material));
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            if (PreviewLayer != null)
            {
                DisplayMaterial material;
                if (this.Attributes.GetTopLevel.Selected)
                {
                    material = args.ShadeMaterial_Selected;
                }
                else material = args.ShadeMaterial;
                    args.Display.DisplayPipelineAttributes.ShadingEnabled = true;
                PreviewLayer.Draw(new RhinoRenderingParameters(args.Display, material));
            }
        }

        #endregion

    }
}