import matplotlib.pyplot as plt
import csv

def plot_application (username, application, feature, i):
    path = r"C:\Users\Simon\OneDrive\Documents\Masters\Module - Thesis\Dissertation\Samples\participant_%s_%s_Samples_%s.csv" 
    path = path % (username, application, feature)
    x = []
    y = []
    count = 0
    total = 0
    mean = 0
    try:
        with open(path, 'r') as file:
            rows = csv.reader(file, delimiter=',')
            next(rows, None)
            for row in rows:
                i=i+1
                x.append(i)
                y.append(float(row[4]))
                total = total + float(row[4])
                count = count + 1
            mean = total / count
    except:
        pass
    return x,y,mean,i

features = ["dwell"]
for feature in features:
    for participant in range(1,9):
        i = 0
        for application in ["chrome","wordpad","notepad"]:
            i = 0
            h = i
            x, y, mean, i = plot_application(participant,application,feature, i)
            plt.plot(x, y, label=application)
            if mean > 0:
                if application == "chrome":
                    color = "purple"
                elif application == "wordpad":
                    color = "red"
                else:
                    color = "black"
                plt.hlines(mean, h, i, colors=color, label="MEAN - %s" % application)
        plt.title("%s - participant_%s" % (feature, participant))
        plt.legend(ncol=2, loc=9)
        outpath = r"c:\kd_img_out\OVERLAPPED - %s-%s.png" % (feature, participant)
        plt.savefig(outpath)
        plt.clf()
