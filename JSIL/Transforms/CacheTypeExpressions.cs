﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JSIL.Ast;
using JSIL.Internal;
using Mono.Cecil;

namespace JSIL.Transforms {
    public class TypeExpressionCacher : JSAstVisitor {
        public readonly Dictionary<GenericTypeIdentifier, JSCachedType> CachedTypes;
        public readonly TypeReference ThisType;
        private int NextID = 0;

        public TypeExpressionCacher (TypeReference thisType) {
            ThisType = thisType;
            CachedTypes = new Dictionary<GenericTypeIdentifier, JSCachedType>();
        }

        private JSCachedType MakeCachedType (TypeReference type) {
            return new JSCachedType(type, NextID++);
        }

        private JSCachedType GetCachedType (TypeReference type) {
            if (!IsCacheable(type))
                return null;

            var resolved = TypeUtil.GetTypeDefinition(type);
            if (resolved == null)
                return null;

            TypeDefinition[] arguments;
            var git = type as GenericInstanceType;

            if (git != null) {
                arguments = (from a in git.GenericArguments select TypeUtil.GetTypeDefinition(a)).ToArray();
            } else {
                arguments = new TypeDefinition[0];
            }

            var identifier = new GenericTypeIdentifier(resolved, arguments);
            JSCachedType result;
            if (!CachedTypes.TryGetValue(identifier, out result))
                CachedTypes.Add(identifier, result = MakeCachedType(type));

            return result;
        }

        public bool IsCacheable (TypeReference type) {
            if (TypeUtil.TypesAreEqual(type, ThisType))
                return false;

            /*
            // Referring to an enclosing type from a nested type creates a cycle
            if (TypeUtil.IsNestedInside(type, ThisType))
                return false;
             */

            if (TypeUtil.ContainsGenericParameter(type))
                return false;

            if (TypeUtil.IsOpenType(type))
                return false;

            return true;
        }

        public void VisitNode (JSType type) {
            var ct = GetCachedType(type.Type);

            if (ct != null) {
                ParentNode.ReplaceChild(type, ct);
                VisitReplacement(ct);
            } else {
                VisitChildren(type);
            }
        }

        public void VisitNode (JSTypeReference tr) {
            VisitChildren(tr);
        }

        public void VisitNode (JSTypeOfExpression toe) {
            // TODO: Cache these types too.
            VisitChildren(toe);
        }

        public void VisitNode (JSCachedType ct) {
            VisitChildren(ct);
        }

        public void VisitNode (JSIsExpression @is) {
            var ct = GetCachedType(@is.Type);
            if (ct != null)
                @is.CachedTypeIndex = ct.Index;

            VisitChildren(@is);
        }

        public void VisitNode (JSCastExpression cast) {
            var ct = GetCachedType(cast.NewType);
            if (ct != null)
                cast.CachedTypeIndex = ct.Index;

            VisitChildren(cast);
        }

        public JSCachedType[] CacheTypesForFunction (JSFunctionExpression function) {
            var currentKeys = new HashSet<GenericTypeIdentifier>(CachedTypes.Keys);

            Visit(function);

            var newKeys = new HashSet<GenericTypeIdentifier>(CachedTypes.Keys);
            newKeys.ExceptWith(currentKeys);

            return (from k in newKeys 
                    let ct = CachedTypes[k]
                    orderby ct.Index
                    select ct).ToArray();
        }
    }
}
