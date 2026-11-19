package com.hoktool.mobile;
public final class HoKFileSignature {
 private HoKFileSignature(){}
 public static String identify(byte[] b,int n){
  if(b==null||n<0||n>b.length)throw new IllegalArgumentException("header length");
  if(n>=7&&new String(b,0,7).equals("UnityFS"))return "UnityFS";
  if(n>=4&&b[0]=='P'&&b[1]=='K')return "ZIP";
  if(n>=4&&(b[0]&255)==1&&b[1]==0&&b[2]==0&&(b[3]&255)==2)return "HoK DB INDEX";
  if(n>=4&&new String(b,0,4).equals("QTSF"))return "QTSF";
  return "unknown";
 }
}