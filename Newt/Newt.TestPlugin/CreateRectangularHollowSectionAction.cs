﻿using Nucleus.Actions;
using Nucleus.Geometry;
using Nucleus.Model;
using Salamander.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Salamander.BasicTools
{
    [Action(
        "CreateRectangularHollowSection",
        "Create a new section property with a rectangular hollow profile",
        IconBackground = Resources.URIs.RectangularHollowSection,
        IconForeground = Resources.URIs.AddIcon)]
    public class CreateRectangularHollowSectionAction : ModelActionBase
    {

        [ActionInput(1, "the name of the section")]
        public string Name { get; set; } = "Rectangular Hollow Section";

        [ActionInput(2, "the depth of the section", Manual = false)]
        public double Depth { get; set; } = 0.2;

        [ActionInput(3, "the width of the section", Manual = false)]
        public double Width { get; set; } = 0.2;

        [ActionInput(4, "the thickness of the section flanges", Manual = false)]
        public double FlangeThickness { get; set; } = 0.01;

        [ActionInput(5, "the thickness of the section web", Manual = false)]
        public double WebThickness { get; set; } = 0.01;

        [ActionInput(7, "the material of the section", Required = false, Manual = false)]
        public Material Material { get; set; }

        [ActionOutput(1, "the output section property")]
        public SectionFamily Section { get; set; }

        [ActionOutput(2, "the output section perimeter")]
        public Curve Perimeter
        {
            get { return Section?.Profile?.Perimeter; }
        }

        [ActionOutput(3, "the output section internal void perimeter")]
        public Curve Void
        {
            get { return Section?.Profile?.Voids?.FirstOrDefault(); }
        }

        public override bool Execute(ExecutionInfo exInfo = null)
        {
            var rProfile = new RectangularHollowProfile(Depth, Width, FlangeThickness, WebThickness);
            rProfile.Material = Material;

            Section = Model.Create.SectionFamily(Name, exInfo);
            Section.Profile = rProfile;
            return true;
        }

        public override bool PostExecutionOperations(ExecutionInfo exInfo = null)
        {
            if (exInfo == null && Section != null)
            {
                // Select the new section
                Core.Instance.Selected.Select(Section);
            }
            return base.PostExecutionOperations(exInfo);
        }
    }
}
