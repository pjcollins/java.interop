﻿using System;
using System.Collections.Generic;

namespace Java.Interop
{
	partial class JniPeerMembers {
	public sealed partial class JniInstanceMethods
	{
		internal JniInstanceMethods (JniPeerMembers members)
		{
			DeclaringType   = members.ManagedPeerType;
			Members         = members;
		}

		JniInstanceMethods (Type declaringType)
		{
			var jvm     = JniEnvironment.Runtime;
			var info    = jvm.TypeManager.GetTypeSignature (declaringType);
			if (info.SimpleReference == null)
				throw new NotSupportedException (
						string.Format ("Cannot create instance of type '{0}': no Java peer type found.",
							declaringType.FullName));

			DeclaringType   = declaringType;
			jniPeerType     = new JniType (info.Name);
			jniPeerType.RegisterWithRuntime ();
		}

		JniPeerMembers                                      Members;
		JniType                                             jniPeerType;

		internal    JniType                                 JniPeerType {
			get {return jniPeerType ?? Members.JniPeerType;}
		}

		readonly Type                                       DeclaringType;

		Dictionary<string, JniInstanceMethodInfo>           InstanceMethods = new Dictionary<string, JniInstanceMethodInfo>();
		Dictionary<Type, JniInstanceMethods>                SubclassConstructors = new Dictionary<Type, JniInstanceMethods> ();

		internal void Dispose ()
		{
			if (InstanceMethods == null)
				return;

			// Don't dispose JniPeerType; it's shared with others.
			InstanceMethods         = null;

			foreach (var p in SubclassConstructors.Values)
				p.Dispose ();
			SubclassConstructors    = null;

			if (jniPeerType != null)
				jniPeerType.Dispose ();
			jniPeerType = null;
		}

		public JniInstanceMethodInfo GetConstructor (string signature)
		{
			if (signature == null)
				throw new ArgumentNullException ("signature");
			lock (InstanceMethods) {
				JniInstanceMethodInfo m;
				if (!InstanceMethods.TryGetValue (signature, out m)) {
					m = JniPeerType.GetConstructor (signature);
					InstanceMethods.Add (signature, m);
				}
				return m;
			}
		}

		internal JniInstanceMethods GetConstructorsForType (Type declaringType)
		{
			if (declaringType == DeclaringType)
				return this;

			JniInstanceMethods methods;
			lock (SubclassConstructors) {
				if (!SubclassConstructors.TryGetValue (declaringType, out methods)) {
					methods = new JniInstanceMethods (declaringType);
					SubclassConstructors.Add (declaringType, methods);
				}
			}
			return methods;
		}

		public JniInstanceMethodInfo GetMethodInfo (string encodedMember)
		{
			lock (InstanceMethods) {
				JniInstanceMethodInfo m;
				if (!InstanceMethods.TryGetValue (encodedMember, out m)) {
					string method, signature;
					JniPeerMembers.GetNameAndSignature (encodedMember, out method, out signature);
					m = JniPeerType.GetInstanceMethod (method, signature);
					InstanceMethods.Add (encodedMember, m);
				}
				return m;
			}
		}

		public unsafe JniObjectReference StartCreateInstance (string constructorSignature, Type declaringType, JniArgumentValue* parameters)
		{
			if (JniEnvironment.Runtime.NewObjectRequired) {
				return NewObject (constructorSignature, declaringType, parameters);
			}
			var r   = GetConstructorsForType (declaringType)
				.JniPeerType
				.AllocObject ();
			r.Flags = JniObjectReferenceFlags.Alloc;
			return r;
		}

		internal JniObjectReference AllocObject (Type declaringType)
		{
			var r   = GetConstructorsForType (declaringType)
				.JniPeerType
				.AllocObject ();
			r.Flags = JniObjectReferenceFlags.Alloc;
			return r;
		}

		internal unsafe JniObjectReference NewObject (string constructorSignature, Type declaringType, JniArgumentValue* parameters)
		{
			var methods = GetConstructorsForType (declaringType);
			var ctor    = methods.GetConstructor (constructorSignature);
			return methods.JniPeerType.NewObject (ctor, parameters);
		}

		public unsafe void FinishCreateInstance (string constructorSignature, IJavaPeerable self, JniArgumentValue* parameters)
		{
			if (JniEnvironment.Runtime.NewObjectRequired) {
				return;
			}
			var methods = GetConstructorsForType (self.GetType ());
			var ctor    = methods.GetConstructor (constructorSignature);
			ctor.InvokeNonvirtualVoidMethod (self.PeerReference, methods.JniPeerType.PeerReference, parameters);
		}
	}
	}

#if !XA_INTEGRATION
	struct JniArgumentMarshalInfo<T> {
		JniArgumentValue                  jvalue;
		JniObjectReference              lref;
		IJavaPeerable                     obj;
		Action<IJavaPeerable, object>     cleanup;

		internal JniArgumentMarshalInfo (T value)
		{
			this        = new JniArgumentMarshalInfo<T> ();
			var jvm     = JniEnvironment.Runtime;
			var info    = jvm.GetJniMarshalInfoForType (typeof (T));
			if (info.CreateJniArgumentValue != null)
				jvalue = info.CreateJniArgumentValue (value);
			else if (info.CreateMarshalCollection != null) {
				obj     = info.CreateMarshalCollection (value);
				jvalue  = new JniArgumentValue (obj);
			} else if (info.CreateLocalRef != null) {
				lref    = info.CreateLocalRef (value);
				jvalue  = new JniArgumentValue (lref);
			} else
				throw new NotSupportedException ("Don't know how to get a JniArgumentValue for: " + typeof (T).FullName);
			cleanup     = info.CleanupMarshalCollection;
		}

		public JniArgumentValue JniArgumentValue {
			get {return jvalue;}
		}

		public  void    Cleanup (object value)
		{
			if (cleanup != null && obj != null)
				cleanup (obj, value);
			JniObjectReference.Dispose (ref lref, JniObjectReferenceOptions.DisposeSourceReference);
		}
	}
#endif  // !XA_INTEGRATION
}

