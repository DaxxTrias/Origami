﻿using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace Origami
{
    internal static class Program
    {
        private static byte[] _payload;
        private static string _identifier;

        private static void Main( string[] args )
        {
            Console.WriteLine( "Origami by drakonia - https://github.com/dr4k0nia/Origami \r\n" );
            if ( args.Length == 0 )
            {
                Console.WriteLine( "Usage: Origami.exe <file>" );
                Console.ReadKey();
                return;
            }

            string file = args[0];

            if ( !File.Exists( file ) )
                throw new FileNotFoundException( $"Could not find file: {file}" );

            var originModule = ModuleDefMD.Load( file );

            if ( !originModule.IsExecutable() )
                throw new InvalidDataException( "Invalid file format => supported are .net executables" );

            //input file as payload
            _payload = File.ReadAllBytes( file );

            //Generate stub based on origin file
            Console.WriteLine( "Generating new stub module" );
            var stubModule = originModule.GetStub();

            _identifier = Utils.GetUniqueName();

            ModifyModule( stubModule );


            //Rename Global Constructor
            var moduleGlobalType = stubModule.GlobalType;
            moduleGlobalType.Name = "Origami";

            stubModule.ScrambleNames();

            stubModule.EntryPoint.Name = _identifier;

            var writerOptions = new ModuleWriterOptions( stubModule );

            writerOptions.WriterEvent += OnWriterEvent;

            stubModule.Write( file.Replace( ".exe", "_origami.exe" ), writerOptions );

            Console.WriteLine( "Saving module..." );
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine( "Finished" );

            Console.ReadKey();
        }

        private static void OnWriterEvent( object sender, ModuleWriterEventArgs e )
        {
            var writer = (ModuleWriterBase) sender;
            if ( e.Event == ModuleWriterEvent.PESectionsCreated )
            {
                var section = new PESection( _identifier.Substring(0, 8), 0xC0000080 /*0x40000080*/ );

                writer.AddSection( section );

                Console.WriteLine( $"Created new pe section {section.Name} with characteristics {section.Characteristics:X}" );

                section.Add( new ByteArrayChunk( _payload.Compress(_identifier) ), 4 );

                Console.WriteLine( $"Wrote {_payload.Length.ToString()} bytes to section {section.Name}" );
            }
        }

        #region "Binary modifications"

        //Inspired by EOFAntiTamper
        private static void ModifyModule( ModuleDef module )
        {
            var loaderType = typeof(Loader);
            //Declare module to inject
            var injectModule = ModuleDefMD.Load( loaderType.Module );
            //Get global constructor or create one if it does not already exist
            var global = module.GlobalType.FindOrCreateStaticConstructor();
            //Declare CallLoader as a TypeDef using it's Metadata token
            var injectType = injectModule.ResolveTypeDef( MDToken.ToRID( loaderType.MetadataToken ) );

            //Use ConfuserEx InjectHelper class to inject Loader class into our target, under <Module>
            var members = InjectHelper.Inject( injectType, module.GlobalType, module );

            Console.WriteLine( $"Creating EntryPoint for stub {module.GlobalType.Name}" );
            //Resolve method for the EntryPoint
            var entryPoint = members.OfType<MethodDef>().Single( method => method.Name == "Main" );
            //Set EntryPoint to Main method defined in the Loader class
            module.EntryPoint = entryPoint;

            //Add STAThreadAttribute
            var attrType = module.CorLibTypes.GetTypeRef( "System", "STAThreadAttribute" );
            var ctorSig = MethodSig.CreateInstance( module.CorLibTypes.Void );
            entryPoint.CustomAttributes.Add( new CustomAttribute(
                new MemberRefUser( module, ".ctor", ctorSig, attrType ) ) );

            // Remove .cctor method because otherwise it will
            // lead to 2 .cctor methods in the stub module
            foreach ( var md in module.GlobalType.Methods )
            {
                if ( md.Name != ".cctor" ) continue;
                module.GlobalType.Remove( md );
                break;
            }
        }

        #endregion
    }
}