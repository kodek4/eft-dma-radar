using eft_dma_shared.Common.Unity;
using SkiaSharp;
using System;
using System.Numerics;

namespace eft_dma_shared.Common.Misc
{
    /// <summary>
    /// Utility class for managing height-aware alpha adjustments for entities and players
    /// </summary>
    public static class HeightAwareAlphaManager
    {
        /// <summary>
        /// Calculate alpha value based on height difference and configuration
        /// </summary>
        /// <param name="entityPosition">Position of the entity</param>
        /// <param name="localPlayerPosition">Position of the local player</param>
        /// <param name="enabled">Whether height-aware alpha is enabled</param>
        /// <param name="dynamicGradient">Whether to use dynamic gradient</param>
        /// <param name="minAlpha">Minimum alpha value (0.0 to 1.0)</param>
        /// <param name="heightThreshold">Height threshold for triggering alpha reduction</param>
        /// <param name="maxGradientDistance">Maximum distance for gradient calculation</param>
        /// <returns>Alpha value between 0.0 and 1.0</returns>
        public static float CalculateAlpha(
            Vector3 entityPosition,
            Vector3 localPlayerPosition,
            bool enabled,
            bool dynamicGradient,
            float minAlpha,
            float heightThreshold = 2.0f,
            float maxGradientDistance = 8.0f)
        {
            if (!enabled)
                return 1.0f;

            var heightDiff = Math.Abs(entityPosition.Y - localPlayerPosition.Y);

            // If within threshold, no alpha reduction
            if (heightDiff <= heightThreshold)
                return 1.0f;

            // Use the configured minimum alpha value directly
            var effectiveMinAlpha = Math.Max(minAlpha, 0.0f); // Only ensure it's not negative

            // If dynamic gradient is disabled, return effective min alpha for anything above threshold
            if (!dynamicGradient)
                return effectiveMinAlpha;

            // Calculate gradient alpha based on distance beyond threshold
            var excessHeight = heightDiff - heightThreshold;
            var gradientFactor = Math.Min(excessHeight / maxGradientDistance, 1.0f);
            
            // Use a smoother curve to prevent harsh transitions to very low alpha
            // Apply square root to make the transition more gradual
            gradientFactor = (float)Math.Sqrt(gradientFactor);
            
            // Interpolate between 1.0 (at threshold) and effectiveMinAlpha (at max distance)
            var alpha = 1.0f - (gradientFactor * (1.0f - effectiveMinAlpha));
            var finalAlpha = Math.Max(alpha, effectiveMinAlpha);
            
            return finalAlpha;
        }

        /// <summary>
        /// Apply height-aware alpha to an SKPaint object, creating a new paint with modified alpha
        /// </summary>
        /// <param name="originalPaint">Original paint object</param>
        /// <param name="alpha">Alpha value to apply (0.0 to 1.0)</param>
        /// <returns>New SKPaint object with modified alpha</returns>
        public static SKPaint ApplyAlphaToPaint(SKPaint originalPaint, float alpha)
        {
            // Null safety and early return checks
            if (originalPaint == null || alpha >= 1.0f)
                return originalPaint;

            // Clamp alpha to valid range
            alpha = Math.Max(0.0f, Math.Min(1.0f, alpha));
            
            // Calculate new alpha byte value
            var originalAlpha = originalPaint.Color.Alpha;
            var newAlpha = (byte)(originalAlpha * alpha);
            
            // Create new paint with all original properties copied manually
            var newPaint = new SKPaint()
            {
                Color = originalPaint.Color.WithAlpha(newAlpha),
                Style = originalPaint.Style,
                StrokeWidth = originalPaint.StrokeWidth,
                IsAntialias = originalPaint.IsAntialias,
                TextSize = originalPaint.TextSize,
                Typeface = originalPaint.Typeface,
                BlendMode = SKBlendMode.SrcOver,  // Force proper alpha blending
                FilterQuality = originalPaint.FilterQuality,
                IsStroke = originalPaint.IsStroke,
                StrokeCap = originalPaint.StrokeCap,
                StrokeJoin = originalPaint.StrokeJoin,
                StrokeMiter = originalPaint.StrokeMiter,
                TextAlign = originalPaint.TextAlign,
                TextEncoding = originalPaint.TextEncoding,
                SubpixelText = originalPaint.SubpixelText
            };
            
            return newPaint;
        }

        /// <summary>
        /// Simplified helper for entities that use a 2-tuple paint pattern (shape, text).
        /// Automatically applies config settings with provided outline paints.
        /// </summary>
        /// <param name="originalPaints">Tuple of (shapeFill, textFill) paints from GetPaints()</param>
        /// <param name="shapeOutline">Shape outline paint (e.g., SKPaints.ShapeOutline)</param>
        /// <param name="textOutline">Text outline paint (e.g., SKPaints.TextOutline)</param>
        /// <param name="entityPosition">The world position of the entity</param>
        /// <param name="localPlayerPosition">The world position of the local player</param>
        /// <param name="config">Height-aware alpha config object</param>
        /// <returns>4-tuple with alpha-adjusted paints</returns>
        public static (SKPaint ShapeOutline, SKPaint ShapeFill, SKPaint TextOutline, SKPaint TextFill) 
            GetEntityPaintsFromTuple(
                (SKPaint shapeFill, SKPaint textFill) originalPaints,
                SKPaint shapeOutline,
                SKPaint textOutline,
                Vector3 entityPosition,
                Vector3 localPlayerPosition,
                dynamic config)
        {
            return GetEntityPaints(
                (shapeOutline, originalPaints.shapeFill, textOutline, originalPaints.textFill),
                entityPosition,
                localPlayerPosition,
                config.EntityEnabled,
                config.EntityDynamicGradient,
                config.EntityMinAlpha,
                config.HeightThreshold,
                config.MaxGradientDistance
            );
        }

        /// <summary>
        /// Simplified helper for entities that use a single paint for shape.
        /// Automatically applies config settings with provided outline paints.
        /// </summary>
        /// <param name="shapeFillPaint">Single shape fill paint</param>
        /// <param name="textFillPaint">Single text fill paint</param>
        /// <param name="shapeOutline">Shape outline paint (e.g., SKPaints.ShapeOutline)</param>
        /// <param name="textOutline">Text outline paint (e.g., SKPaints.TextOutline)</param>
        /// <param name="entityPosition">The world position of the entity</param>
        /// <param name="localPlayerPosition">The world position of the local player</param>
        /// <param name="config">Height-aware alpha config object</param>
        /// <returns>4-tuple with alpha-adjusted paints</returns>
        public static (SKPaint ShapeOutline, SKPaint ShapeFill, SKPaint TextOutline, SKPaint TextFill) 
            GetEntityPaintsFromSingle(
                SKPaint shapeFillPaint,
                SKPaint textFillPaint,
                SKPaint shapeOutline,
                SKPaint textOutline,
                Vector3 entityPosition,
                Vector3 localPlayerPosition,
                dynamic config)
        {
            return GetEntityPaints(
                (shapeOutline, shapeFillPaint, textOutline, textFillPaint),
                entityPosition,
                localPlayerPosition,
                config.EntityEnabled,
                config.EntityDynamicGradient,
                config.EntityMinAlpha,
                config.HeightThreshold,
                config.MaxGradientDistance
            );
        }

        /// <summary>
        /// Get alpha-adjusted paints for entities with shape and text elements.
        /// This is the core method that all other entity paint methods use.
        /// </summary>
        /// <param name="originalPaints">Tuple of (shapeOutline, shapeFill, textOutline, textFill) paints</param>
        /// <param name="entityPosition">The world position of the entity</param>
        /// <param name="localPlayerPosition">The world position of the local player</param>
        /// <param name="enabled">Whether height-aware alpha is enabled</param>
        /// <param name="dynamicGradient">Whether to use dynamic gradient</param>
        /// <param name="minAlpha">Minimum alpha value</param>
        /// <param name="heightThreshold">Height threshold for triggering alpha reduction</param>
        /// <param name="maxGradientDistance">Maximum distance for gradient calculation</param>
        /// <returns>Tuple of alpha-adjusted paints (shapeOutline, shapeFill, textOutline, textFill)</returns>
        public static (SKPaint ShapeOutline, SKPaint ShapeFill, SKPaint TextOutline, SKPaint TextFill) GetEntityPaints(
            (SKPaint shapeOutline, SKPaint shapeFill, SKPaint textOutline, SKPaint textFill) originalPaints,
            Vector3 entityPosition,
            Vector3 localPlayerPosition,
            bool enabled,
            bool dynamicGradient,
            float minAlpha,
            float heightThreshold = 2.0f,
            float maxGradientDistance = 8.0f)
        {
            if (!enabled)
                return originalPaints;
                
            float alpha = CalculateAlpha(entityPosition, localPlayerPosition, enabled, dynamicGradient, minAlpha, heightThreshold, maxGradientDistance);
            
            return (
                ApplyAlphaToPaint(originalPaints.shapeOutline, alpha),
                ApplyAlphaToPaint(originalPaints.shapeFill, alpha),
                ApplyAlphaToPaint(originalPaints.textOutline, alpha),
                ApplyAlphaToPaint(originalPaints.textFill, alpha)
            );
        }

        /// <summary>
        /// Simplified helper for players that use a 2-tuple paint pattern (shape, text).
        /// Automatically applies player config settings with provided outline paints.
        /// </summary>
        /// <param name="originalPaints">Tuple of (shapeFill, textFill) paints from GetPaints()</param>
        /// <param name="shapeOutline">Shape outline paint (e.g., SKPaints.ShapeOutline)</param>
        /// <param name="textOutline">Text outline paint (e.g., SKPaints.TextOutline)</param>
        /// <param name="playerPosition">The world position of the player</param>
        /// <param name="localPlayerPosition">The world position of the local player</param>
        /// <param name="config">Height-aware alpha config object</param>
        /// <returns>4-tuple with alpha-adjusted paints</returns>
        public static (SKPaint ShapeOutline, SKPaint ShapeFill, SKPaint TextOutline, SKPaint TextFill) 
            GetPlayerPaintsFromTuple(
                (SKPaint shapeFill, SKPaint textFill) originalPaints,
                SKPaint shapeOutline,
                SKPaint textOutline,
                Vector3 playerPosition,
                Vector3 localPlayerPosition,
                dynamic config)
        {
            return GetEntityPaints(
                (shapeOutline, originalPaints.shapeFill, textOutline, originalPaints.textFill),
                playerPosition,
                localPlayerPosition,
                config.PlayerEnabled,
                config.PlayerDynamicGradient,
                config.PlayerMinAlpha,
                config.HeightThreshold,
                config.MaxGradientDistance
            );
        }
    }
} 