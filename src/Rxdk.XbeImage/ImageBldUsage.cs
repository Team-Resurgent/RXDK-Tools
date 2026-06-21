namespace Rxdk.XbeImage;

public static class ImageBldUsage
{
    public static void Print(TextWriter writer)
    {
        writer.WriteLine("usage: IMAGEBLD [options] [infile]");
        writer.WriteLine("  /DEFAULTSAVEIMAGE:imagefile\tDefault Save Image (XPR format) for this image");
        writer.WriteLine("  /DONTMODIFYHD\t\t\tNo hard disk writing (no T: and U: drives)");
        writer.WriteLine("  /DONTMOUNTUD\t\t\tInitialize without mounting a utility drive");
        writer.WriteLine("  /IN:infile\t\t\tInput PE file to process");
        writer.WriteLine("  /INITFLAGS:number\t\tInitialization flags (default=0x00000001)");
        writer.WriteLine("  /INSERTFILE:file,sec,[R][N]\t*Insert file as section");
        writer.WriteLine("  /FORMATUD\t\t\tAlways format utility drive mounted during init");
        writer.WriteLine("  /LIMITMEM\t\t\tLimit runtime memory to 64MB");
        writer.WriteLine("  /NOLIBWARN\t\t\tDisable unapproved library warning messages");
        writer.WriteLine("  /NOPRELOAD:sec\t\t*Don't automatically load this section");
        writer.WriteLine("  /OUT:outfile\t\t\tOutput XBE image file");
        writer.WriteLine("  /STACK:size\t\t\tSet size of stack");
        writer.WriteLine("  /TESTALTID:number[,key]\t*Test Alt ID for access by alternate images");
        writer.WriteLine("  /TESTID:number\t\tTest Title ID for this image");
        writer.WriteLine("  /TESTLANKEY:key\t\tTest LAN key for this image");
        writer.WriteLine("  /TESTMEDIATYPES:number\tTest allowed media types for this image");
        writer.WriteLine("  /TESTNAME:titlename\t\tTest title name for this image");
        writer.WriteLine("  /TESTRATINGS:number\t\tTest ratings for this image");
        writer.WriteLine("  /TESTREGION:number\t\tTest allowed regions for this image");
        writer.WriteLine("  /TESTSIGNKEY:key\t\tTest signature key for this image");
        writer.WriteLine("  /TESTVERSION:number\t\tTest version number for this image");
        writer.WriteLine("  /TITLEIMAGE:imagefile\t\tTitle Image (XPR format) for this image");
        writer.WriteLine("  /TITLEINFO:infofile\t\tTitle Info file for this image");
        writer.WriteLine("  /UDCLUSTER:number\t\tUtility drive cluster size (default=16384)");
        writer.WriteLine("  /?\t\t\t\tDisplay this message");
        writer.WriteLine();
        writer.WriteLine("*=multiples allowed");
        writer.WriteLine();
        writer.WriteLine("usage: IMAGEBLD /DUMP [file]");
    }
}
