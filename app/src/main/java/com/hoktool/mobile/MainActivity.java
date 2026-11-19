package com.hoktool.mobile;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.ViewGroup;
import android.widget.*;
import java.io.*;
import java.util.Locale;
import java.util.zip.*;

public class MainActivity extends Activity {
 private static final int OPEN=1, SAVE=2, BMS=3;
 private Uri selected;
 private String name="Chưa chọn tệp", report="Chưa có báo cáo";
 private TextView status,file,result;
 private final int bg=Color.rgb(10,17,27), panel=Color.rgb(18,29,43), ink=Color.rgb(236,244,250), muted=Color.rgb(153,174,191), accent=Color.rgb(83,214,208);
 public void onCreate(Bundle b){super.onCreate(b);getWindow().setStatusBarColor(bg);getWindow().setNavigationBarColor(bg);ui();}
 private void ui(){
  ScrollView s=new ScrollView(this); LinearLayout root=new LinearLayout(this);root.setOrientation(1);root.setPadding(18,20,18,28);root.setBackgroundColor(bg);s.addView(root);
  add(root,"HONOR OF KINGS · ANDROID",12,accent,true);add(root,"HoK Tool",32,ink,true);add(root,"Kiểm tra dữ liệu game và hỗ trợ QuickBMS",14,muted,false);
  status=add(root,"● Sẵn sàng",13,accent,true);
  LinearLayout c=card();root.addView(c,lp(12));add(c,"01 · DỮ LIỆU ĐẦU VÀO",17,ink,true);file=add(c,name,14,muted,true);button(c,"Chọn tệp HoK",true,()->pick(OPEN));button(c,"Phân tích lại",false,()->analyze());
  LinearLayout d=card();root.addView(d,lp(12));add(d,"02 · KẾT QUẢ PHÂN TÍCH",17,ink,true);result=add(d,"Chọn tệp .db, .assetbundle, .bytes hoặc .zip để bắt đầu.",13,muted,false);result.setTypeface(Typeface.MONOSPACE);
  LinearLayout e=card();root.addView(e,lp(12));add(e,"03 · CÔNG CỤ HỖ TRỢ",17,ink,true);button(e,"Xuất báo cáo",true,()->{if(report.equals("Chưa có báo cáo")){status.setText("Chưa có báo cáo");return;}pick(SAVE);});button(e,"Xuất script DB · BMS",false,()->pick(BMS));
  LinearLayout f=card();root.addView(f);add(f,"GIỚI HẠN HIỆN TẠI",16,Color.rgb(255,190,92),true);add(f,"Bản này kiểm tra header/ZIP và xuất script BMS. Chưa phải bản port đầy đủ AssetStudio: chưa duyệt/xuất Texture2D, Model, Audio, chỉnh sửa AssetBundle hoặc giải mã Oodle. Nhận diện DB không đồng nghĩa đã giải nén DB.",13,muted,false);
  setContentView(s);
 }
 private LinearLayout card(){LinearLayout x=new LinearLayout(this);x.setOrientation(1);x.setPadding(16,16,16,16);GradientDrawable g=new GradientDrawable();g.setColor(panel);g.setCornerRadius(18);x.setBackground(g);return x;}
 private TextView add(LinearLayout p,String v,int z,int color,boolean bold){TextView t=new TextView(this);t.setText(v);t.setTextSize(z);t.setTextColor(color);if(bold)t.setTypeface(Typeface.DEFAULT,Typeface.BOLD);t.setPadding(0,5,0,9);p.addView(t);return t;}
 private void button(LinearLayout p,String label,boolean primary,Runnable r){Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setOnClickListener(v->r.run());p.addView(b,new LinearLayout.LayoutParams(-1,-2));}
 private LinearLayout.LayoutParams lp(int bottom){LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.bottomMargin=bottom;return p;}
 private void pick(int req){Intent i=new Intent(req==OPEN?Intent.ACTION_OPEN_DOCUMENT:Intent.ACTION_CREATE_DOCUMENT);i.addCategory(Intent.CATEGORY_OPENABLE);i.setType(req==SAVE?"text/plain":"*/*");if(req==BMS){i.setType("application/octet-stream");i.putExtra(Intent.EXTRA_TITLE,"honor-of-kings_db_extract_v3.bms");}if(req==SAVE)i.putExtra(Intent.EXTRA_TITLE,"hok_analysis_report.txt");startActivityForResult(i,req);}
 protected void onActivityResult(int q,int r,Intent d){super.onActivityResult(q,r,d);if(r!=RESULT_OK||d==null||d.getData()==null)return;Uri u=d.getData();try{if(q==OPEN){selected=u;name=queryName(u);file.setText(name);analyze();}else if(q==SAVE){try(OutputStream o=getContentResolver().openOutputStream(u)){o.write(report.getBytes("UTF-8"));}status.setText("Đã xuất báo cáo");}else if(q==BMS){try(InputStream in=getAssets().open("honor-of-kings_db_extract_v3.bms");OutputStream o=getContentResolver().openOutputStream(u)){copy(in,o);}status.setText("Đã xuất script BMS");}}catch(Exception e){status.setText("Lỗi: "+e.getMessage());}}
 private String queryName(Uri u){try(Cursor c=getContentResolver().query(u,null,null,null,null)){if(c!=null&&c.moveToFirst()){int k=c.getColumnIndex(OpenableColumns.DISPLAY_NAME);if(k>=0)return c.getString(k);}}catch(Exception ignored){}return "Tệp đã chọn";}
 private void analyze(){if(selected==null){status.setText("Hãy chọn tệp trước");return;}status.setText("Đang phân tích…");new Thread(()->{String out;try{out=inspect(selected,name);}catch(Exception e){out="PHÂN TÍCH LỖI: "+e;}String value=out;runOnUiThread(()->{report=value;result.setText(value);status.setText("Đã phân tích xong");});}).start();}
 private String inspect(Uri u,String n)throws Exception{byte[] h=new byte[64];int k;try(InputStream in=getContentResolver().openInputStream(u)){k=in.read(h);}StringBuilder x=new StringBuilder("TÊN: "+n+"\nHEADER: ");for(int i=0;i<Math.max(0,k);i++)x.append(String.format(Locale.ROOT,"%02X ",h[i]&255));String type="Chưa nhận diện";if(k>=7&&new String(h,0,7,"US-ASCII").equals("UnityFS"))type="Unity AssetBundle · UnityFS";else if(k>=4&&h[0]=='P'&&h[1]=='K')type="ZIP archive";else if(k>=4&&(h[0]&255)==1&&h[1]==0&&h[2]==0&&(h[3]&255)==2)type="HoK DB INDEX · 0x02000001";else if(k>=4&&new String(h,0,4,"US-ASCII").equals("QTSF"))type="QTSF data";x.append("\n\nĐỊNH DẠNG: ").append(type);if(type.equals("ZIP archive"))x.append("\n").append(zipList(u));x.append("\n\nLưu ý: nhận diện header không phải giải nén hoàn chỉnh. Engine AssetStudio Windows/Oodle chưa được port sang Android.");return x.toString();}
 private String zipList(Uri u){StringBuilder b=new StringBuilder("NỘI DUNG ZIP (tối đa 35 mục):\n");try(InputStream in=getContentResolver().openInputStream(u);ZipInputStream z=new ZipInputStream(in)){ZipEntry e;int i=0;while((e=z.getNextEntry())!=null&&i++<35)b.append(e.getName()).append('\n');}catch(Exception e){b.append("Lỗi đọc ZIP: ").append(e.getMessage());}return b.toString();}
 private void copy(InputStream i,OutputStream o)throws IOException{byte[] b=new byte[8192];int n;while((n=i.read(b))>0)o.write(b,0,n);o.flush();}
}