﻿using Server.MirDatabase;
using Server.MirObjects;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Text;

namespace Server.MirDatabase
{
    public class MailInfo
    {
        [Key]
        public long MailID { get; set; }

        public string Sender { get; set; }
        //[ForeignKey("RecipientInfo")]
        public int RecipientIndex { get; set; }
        
        public CharacterInfo RecipientInfo { get; set; }
        //[ForeignKey("CharacterInfo")]
        public int CharacterIndex { get; set; }
        
        public CharacterInfo CharacterInfo { get; set; }

        public string Message { get; set; } = string.Empty;
        [NotMapped]
        public uint Gold { get; set; } = 0;

        public long DBGold
        {
            get { return Gold; }
            set { Gold = (uint) value; }
        }
        public List<UserItem> Items = new List<UserItem>();

        public DateTime? DateSent { get; set; } = DateTime.MinValue;
        public DateTime? DateOpened { get; set; } = DateTime.MinValue;
        [NotMapped]
        public bool Sent
        {
            get { return DateSent > DateTime.MinValue; }
        }
        [NotMapped]
        public bool Opened
        {
            get { return DateOpened > DateTime.MinValue; }
        }

        public bool Locked { get; set; }

        public bool Collected { get; set; }
        [NotMapped]
        public bool Parcel //parcel if item contains gold or items.
        {
            get { return Gold > 0 || Items.Count > 0; }
        }

        public bool CanReply { get; set; }

        public MailInfo() { }

        public MailInfo(int recipientIndex, bool canReply = false)
        {
            MailID = ++SMain.Envir.NextMailID;
            RecipientIndex = recipientIndex;

            CanReply = canReply;
        }

        public MailInfo(BinaryReader reader, int version, int customversion)
        {
            MailID = (long) reader.ReadUInt64();
            Sender = reader.ReadString();
            RecipientIndex = reader.ReadInt32();
            Message = reader.ReadString();
            Gold = reader.ReadUInt32();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                UserItem item = new UserItem(reader, version, customversion);
                if (SMain.Envir.BindItem(item))
                    Items.Add(item);
            }

            DateSent = DateTime.FromBinary(reader.ReadInt64());
            DateOpened = DateTime.FromBinary(reader.ReadInt64());

            Locked = reader.ReadBoolean();
            Collected = reader.ReadBoolean();
            CanReply = reader.ReadBoolean();
        }

        public void Save(BinaryWriter writer)
        {
            if (Settings.UseSQLServer)
            {
                using (var ctx = new DataContext())
                {
                    var _mail = this;
                    _mail.RecipientIndex = _mail.RecipientInfo?.Index ?? 0;
                    _mail.CharacterIndex = _mail.CharacterInfo?.Index ?? 0;
                    _mail.RecipientInfo = null;
                    _mail.CharacterInfo = null;
                    var dbMail = ctx.Mails.FirstOrDefault(m => m.MailID == MailID);
                    if (dbMail == null)
                    {
                        ctx.Mails.Add(_mail);
                    }
                    else
                    {
                        ctx.Entry(dbMail).CurrentValues.SetValues(_mail);
                    }
                    ctx.SaveChanges();
                    ctx.MailItems.RemoveRange(ctx.MailItems.Where(i => i.MailID == MailID));
                    ctx.SaveChanges();
                    if(Items.Count < 1) return;
                    foreach (var item in Items)
                    {
                        var dbItem = ctx.UserItems.FirstOrDefault(i => i.UniqueID == item.UniqueID);
                        if (dbItem == null)
                        {
                            dbItem = item;
                            ctx.UserItems.Add(dbItem);
                        }
                        else
                        {
                            ctx.Entry(dbItem).CurrentValues.SetValues(item);
                        }
                        ctx.SaveChanges();
                        ctx.MailItems.Add(new MailItem()
                        {
                            MailID = MailID,
                            ItemUniqueID = dbItem.UniqueID
                        });
                        ctx.SaveChanges();
                    }
                }
                return;
            }
            writer.Write(MailID);
            writer.Write(Sender);
            writer.Write(RecipientIndex);
            writer.Write(Message);
            writer.Write(Gold);

            writer.Write(Items.Count);
            for (int i = 0; i < Items.Count; i++)
                Items[i].Save(writer);

            writer.Write(DateSent.GetValueOrDefault().ToBinary());
            writer.Write(DateOpened.GetValueOrDefault().ToBinary());

            writer.Write(Locked);
            writer.Write(Collected);
            writer.Write(CanReply);
        }

        public void Send()
        {
            if (Sent) return;

            Collected = true;

            if (Parcel)
            {
                if(Items.Count > 0 && Gold > 0)
                {
                    if(!Settings.MailAutoSendGold || !Settings.MailAutoSendItems)
                    {
                        Collected = false;
                    }
                }
                if(Items.Count > 0)
                {
                    if (!Settings.MailAutoSendItems)
                    {
                        Collected = false;
                    }
                }
                else
                {
                    if (!Settings.MailAutoSendGold)
                    {
                        Collected = false;
                    }
                }
            }

            if (SMain.Envir.Mail.Contains(this)) return;

            SMain.Envir.Mail.Add(this); //add to postbox

            DateSent = DateTime.Now;
        }

        public bool Receive()
        {
            if (!Sent) return false; //mail not sent yet

            if (RecipientInfo == null)
            {
                RecipientInfo = SMain.Envir.GetCharacterInfo(RecipientIndex);

                if (RecipientInfo == null) return false;
            }

            RecipientInfo.Mail.Add(this); //add to players inbox
            
            if(RecipientInfo.Player != null)
            {
                RecipientInfo.Player.NewMail = true; //notify player of new mail  --check in player process
            }

            SMain.Envir.Mail.Remove(this); //remove from postbox

            return true;
        }

        public ClientMail CreateClientMail()
        {
            return new ClientMail
            {
                MailID = MailID,
                SenderName = Sender,
                Message = Message,
                Locked = Locked,
                CanReply = CanReply,
                Gold = Gold,
                Items = Items,
                Opened = Opened,
                Collected = Collected,
                DateSent = DateSent.GetValueOrDefault()
            };
        }
    }

    public class MailItem
    {
        [Key]
        public long Index { get; set; }
        [ForeignKey("MailInfo")]
        public long MailID { get; set; }
        
        public MailInfo MailInfo { get; set; }
        [ForeignKey("UserItem")]
        public long ItemUniqueID { get; set; }
        
        public UserItem UserItem { get; set; }
    }

    // player bool NewMail (process in envir loop) - send all mail on login

    // Send mail from player (auto from player)
    // Send mail from Envir (mir administrator)
}
